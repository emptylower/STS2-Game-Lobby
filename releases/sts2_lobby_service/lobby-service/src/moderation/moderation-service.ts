import {
  LexiconSensitiveWordFilter,
  nullSensitiveWordFilter,
  type MaskResult,
  type SensitiveMatch,
  type SensitiveWordFilter,
} from "./filter.js";
import { loadLexicon } from "./lexicon-loader.js";

export interface ModerationStats {
  enabled: boolean;
  wordCount: number;
  maskedMessages: number;
  rejectedNames: number;
  loadError: string | null;
  lexiconFingerprint: string;
}

export interface ModerationServiceOptions {
  lexiconDir: string;
  enabled: boolean;
  log?: (message: string) => void;
}

/**
 * 敏感词过滤的运行时持有者：
 * - 启动时加载词库（失败 fail-open，记录 loadError，不阻断服务启动）；
 * - setEnabled 热切换 开/关（关 = NullFilter），挂接点持有稳定引用无感知；
 * - 统计打码/拒绝次数（自进程启动起累计，重启清零）。
 *
 * 注意：maskedMessages 按 maskChatText 调用计数。聊天一条消息的相邻 text
 * segment 会被 canonicalize 合并，通常一条消息只触发一次打码检查；
 * 含 emoji/引用分割的多段消息命中多段时会计多次，属可接受的近似。
 */
export class ModerationService {
  private active: SensitiveWordFilter;
  private maskedMessages = 0;
  private rejectedNames = 0;

  private constructor(
    private readonly lexiconFilter: SensitiveWordFilter,
    private readonly lexiconLoadError: string | null,
    enabled: boolean,
  ) {
    this.active = enabled ? lexiconFilter : nullSensitiveWordFilter;
  }

  static create(options: ModerationServiceOptions): ModerationService {
    try {
      const lexicon = loadLexicon(options.lexiconDir);
      if (lexicon.wordCount === 0) {
        options.log?.(`[moderation] lexicon at ${options.lexiconDir} is empty; sensitive filter unavailable`);
        return new ModerationService(nullSensitiveWordFilter, "词库为空", options.enabled);
      }
      const filter = new LexiconSensitiveWordFilter(
        lexicon.root,
        lexicon.wordCount,
        lexicon.entries,
        lexicon.fingerprint,
      );
      options.log?.(`[moderation] loaded ${lexicon.wordCount} sensitive words from ${options.lexiconDir}`);
      return new ModerationService(filter, null, options.enabled);
    } catch (error) {
      options.log?.(`[moderation] failed to load lexicon from ${options.lexiconDir}: ${String(error)}`);
      return new ModerationService(nullSensitiveWordFilter, "词库加载失败", options.enabled);
    }
  }

  get enabled(): boolean {
    return this.active !== nullSensitiveWordFilter;
  }

  get wordCount(): number {
    return this.lexiconFilter.wordCount;
  }

  setEnabled(enabled: boolean): void {
    this.active = enabled ? this.lexiconFilter : nullSensitiveWordFilter;
  }

  maskChatText(text: string): MaskResult {
    const result = this.active.mask(text);
    if (result.masked) {
      this.maskedMessages += 1;
    }
    return result;
  }

  findSensitiveMatches(text: string): SensitiveMatch[] {
    return this.active.find(text);
  }

  containsSensitiveName(text: string): boolean {
    return this.active.contains(text);
  }

  recordNameRejection(): void {
    this.rejectedNames += 1;
  }

  stats(): ModerationStats {
    return {
      enabled: this.enabled,
      wordCount: this.wordCount,
      maskedMessages: this.maskedMessages,
      rejectedNames: this.rejectedNames,
      loadError: this.lexiconLoadError,
      lexiconFingerprint: this.lexiconFilter.fingerprint,
    };
  }
}
