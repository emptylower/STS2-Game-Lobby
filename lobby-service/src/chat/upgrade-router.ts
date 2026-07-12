import type { IncomingMessage, Server as HttpServer } from "node:http";
import type { Duplex } from "node:stream";
import type { WebSocketServer } from "ws";

/** Fixed server-channel WebSocket path; control path remains configurable via WS_PATH. */
export const CHAT_WS_PATH = "/chat";

export type ChatUpgradeDecision =
  | {
      ok: true;
      /** Called after the WebSocket is fully accepted, immediately before `connection`. */
      commit(): void;
      /** Called when the upgrade fails after a successful authorize decision. */
      release(): void;
    }
  | {
      ok: false;
      statusCode: 401 | 429 | 503;
      retryAfterSeconds?: number;
    };

export interface InstallUpgradeRouterOptions {
  server: HttpServer;
  controlPath: string;
  controlWss: WebSocketServer;
  chatWss: WebSocketServer;
  authorizeChat(req: IncomingMessage): ChatUpgradeDecision;
}

/**
 * Install the single HTTP `upgrade` listener that routes `/control` and `/chat`.
 * Both WSS instances must use `{ noServer: true }` and must not register their own listeners.
 * Returns an uninstall function that removes this router only.
 */
export function installUpgradeRouter(options: InstallUpgradeRouterOptions): () => void {
  const { server, controlPath, controlWss, chatWss, authorizeChat } = options;
  const normalizedControlPath = normalizePathname(controlPath);

  const onUpgrade = (req: IncomingMessage, socket: Duplex, head: Buffer): void => {
    let pathname: string;
    try {
      pathname = parseUpgradePathname(req);
    } catch {
      rejectUpgrade(socket, 400, "Bad Request");
      return;
    }

    if (pathname === normalizedControlPath) {
      controlWss.handleUpgrade(req, socket, head, (ws) => {
        controlWss.emit("connection", ws, req);
      });
      return;
    }

    if (pathname === CHAT_WS_PATH) {
      handleChatUpgrade(req, socket, head, chatWss, authorizeChat);
      return;
    }

    rejectUpgrade(socket, 404, "Not Found");
  };

  server.on("upgrade", onUpgrade);
  return () => {
    server.off("upgrade", onUpgrade);
  };
}

function handleChatUpgrade(
  req: IncomingMessage,
  socket: Duplex,
  head: Buffer,
  chatWss: WebSocketServer,
  authorizeChat: (req: IncomingMessage) => ChatUpgradeDecision,
): void {
  // Preflight: Authorization must be Bearer before any capacity/ticket work.
  if (!hasBearerAuthorization(req)) {
    rejectUpgrade(socket, 401, "Unauthorized");
    return;
  }

  let decision: ChatUpgradeDecision;
  try {
    decision = authorizeChat(req);
  } catch {
    rejectUpgrade(socket, 503, "Service Unavailable");
    return;
  }

  if (!decision.ok) {
    const headers: Record<string, string> = {};
    if (decision.statusCode === 429 && decision.retryAfterSeconds !== undefined) {
      headers["Retry-After"] = String(decision.retryAfterSeconds);
    }
    rejectUpgrade(socket, decision.statusCode, statusText(decision.statusCode), headers);
    return;
  }

  const { commit, release } = decision;
  let settled = false;

  const fail = (): void => {
    if (settled) {
      return;
    }
    settled = true;
    socket.off("error", fail);
    socket.off("close", fail);
    try {
      release();
    } catch {
      // release must not throw into the upgrade path
    }
    destroySocket(socket);
  };

  // If the client aborts before the upgrade callback runs, release the reservation.
  socket.once("error", fail);
  socket.once("close", fail);

  try {
    chatWss.handleUpgrade(req, socket, head, (ws) => {
      if (settled) {
        try {
          ws.terminate();
        } catch {
          // ignore
        }
        return;
      }
      settled = true;
      socket.off("error", fail);
      socket.off("close", fail);
      try {
        // Commit reservation immediately before promoting the connection.
        commit();
      } catch {
        try {
          release();
        } catch {
          // ignore
        }
        try {
          ws.terminate();
        } catch {
          // ignore
        }
        destroySocket(socket);
        return;
      }
      chatWss.emit("connection", ws, req);
    });
  } catch {
    fail();
  }
}

function hasBearerAuthorization(req: IncomingMessage): boolean {
  const value = req.headers.authorization;
  if (typeof value !== "string") {
    return false;
  }
  return /^Bearer\s+\S+/i.test(value.trim());
}

function parseUpgradePathname(req: IncomingMessage): string {
  const rawUrl = req.url;
  if (typeof rawUrl !== "string" || rawUrl.length === 0) {
    throw new Error("missing url");
  }

  const host = typeof req.headers.host === "string" && req.headers.host.length > 0
    ? req.headers.host
    : "127.0.0.1";

  // Absolute-form request targets (e.g. "http://") must still parse cleanly.
  const parsed = new URL(rawUrl, `http://${host}`);
  return normalizePathname(parsed.pathname);
}

function normalizePathname(pathname: string): string {
  if (!pathname) {
    return "/";
  }
  // Collapse trailing slashes except for root.
  if (pathname.length > 1 && pathname.endsWith("/")) {
    return pathname.replace(/\/+$/, "");
  }
  return pathname;
}

function rejectUpgrade(
  socket: Duplex,
  statusCode: number,
  reason: string,
  headers: Record<string, string> = {},
): void {
  if (socket.destroyed || !socket.writable) {
    destroySocket(socket);
    return;
  }

  const headerLines = [
    `HTTP/1.1 ${statusCode} ${reason}`,
    "Connection: close",
    "Content-Length: 0",
    ...Object.entries(headers).map(([name, value]) => `${name}: ${value}`),
    "",
    "",
  ];

  try {
    socket.write(headerLines.join("\r\n"));
  } catch {
    // ignore write races
  }
  destroySocket(socket);
}

function destroySocket(socket: Duplex): void {
  try {
    socket.destroy();
  } catch {
    // ignore
  }
}

function statusText(statusCode: number): string {
  switch (statusCode) {
    case 401:
      return "Unauthorized";
    case 429:
      return "Too Many Requests";
    case 503:
      return "Service Unavailable";
    default:
      return "Error";
  }
}
