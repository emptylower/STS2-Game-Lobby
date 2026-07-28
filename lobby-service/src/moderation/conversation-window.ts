import type { SensitiveMatch } from "./filter.js";

export interface ConversationModerationCandidate {
  text: string;
  relatedMessageIds: string[];
  fragmentCount: number;
}

interface StoredFragment {
  messageId: string | null;
  text: string;
  observedAt: number;
}

export interface ModerationConversationWindowOptions {
  now?: () => number;
  maxMessages?: number;
  ttlMs?: number;
  maxFragmentScalars?: number;
  maxSenders?: number;
}

const DEFAULT_MAX_MESSAGES = 10;
const DEFAULT_TTL_MS = 30_000;
const DEFAULT_MAX_FRAGMENT_SCALARS = 4;
const DEFAULT_MAX_SENDERS = 10_000;

/**
 * Tracks only a sender's trailing short-message burst. Long messages reset the
 * burst, so ordinary conversation is never flattened into an AI prompt. The
 * caller supplies authenticated channel-scoped sender keys; display names are
 * not trusted as identity inside a room.
 */
export class ModerationConversationWindow {
  private readonly now: () => number;
  private readonly maxMessages: number;
  private readonly ttlMs: number;
  private readonly maxFragmentScalars: number;
  private readonly maxSenders: number;
  private readonly bySender = new Map<string, StoredFragment[]>();

  constructor(options: ModerationConversationWindowOptions = {}) {
    this.now = options.now ?? Date.now;
    this.maxMessages = options.maxMessages ?? DEFAULT_MAX_MESSAGES;
    this.ttlMs = options.ttlMs ?? DEFAULT_TTL_MS;
    this.maxFragmentScalars = options.maxFragmentScalars ?? DEFAULT_MAX_FRAGMENT_SCALARS;
    this.maxSenders = options.maxSenders ?? DEFAULT_MAX_SENDERS;
  }

  buildCandidate(
    senderKey: string,
    currentText: string,
    findMatches: (text: string) => readonly SensitiveMatch[],
  ): ConversationModerationCandidate | null {
    if (!this.isShort(currentText)) return null;
    const prior = this.prune(senderKey);
    if (prior.length === 0) return null;
    const fragments = [...prior, {
      messageId: null,
      text: currentText.normalize("NFC"),
      observedAt: this.now(),
    }];
    const boundaries: number[] = [];
    let text = "";
    for (const fragment of fragments) {
      text += fragment.text;
      boundaries.push(text.length);
    }
    boundaries.pop();
    const crossesBoundary = findMatches(text).some((match) =>
      boundaries.some((boundary) => match.start < boundary && match.end > boundary));
    if (!crossesBoundary) return null;
    return {
      text,
      relatedMessageIds: prior.flatMap((fragment) =>
        fragment.messageId === null ? [] : [fragment.messageId]),
      fragmentCount: fragments.length,
    };
  }

  recordCommitted(senderKey: string, messageId: string, text: string): void {
    this.record(senderKey, messageId, text);
  }

  recordBlocked(senderKey: string, text: string): void {
    this.record(senderKey, null, text);
  }

  markRedacted(senderKey: string, messageIds: readonly string[]): void {
    if (messageIds.length === 0) return;
    const ids = new Set(messageIds);
    for (const fragment of this.bySender.get(senderKey) ?? []) {
      if (fragment.messageId !== null && ids.has(fragment.messageId)) fragment.messageId = null;
    }
  }

  clear(): void {
    this.bySender.clear();
  }

  private record(senderKey: string, messageId: string | null, text: string): void {
    const normalized = text.normalize("NFC");
    if (!this.isShort(normalized)) {
      this.bySender.delete(senderKey);
      return;
    }
    const fragments = this.prune(senderKey);
    fragments.push({ messageId, text: normalized, observedAt: this.now() });
    while (fragments.length > this.maxMessages - 1) fragments.shift();
    this.bySender.delete(senderKey);
    this.bySender.set(senderKey, fragments);
    while (this.bySender.size > this.maxSenders) {
      const oldest = this.bySender.keys().next().value as string | undefined;
      if (oldest === undefined) break;
      this.bySender.delete(oldest);
    }
  }

  private prune(senderKey: string): StoredFragment[] {
    const cutoff = this.now() - this.ttlMs;
    const fragments = (this.bySender.get(senderKey) ?? [])
      .filter((fragment) => fragment.observedAt >= cutoff)
      .slice(-(this.maxMessages - 1));
    if (fragments.length === 0) this.bySender.delete(senderKey);
    else this.bySender.set(senderKey, fragments);
    return fragments;
  }

  private isShort(text: string): boolean {
    const count = Array.from(text.trim()).length;
    return count > 0 && count <= this.maxFragmentScalars;
  }
}
