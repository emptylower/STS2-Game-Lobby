export function mergeServerAdminPollSnapshot(
  current: Record<string, unknown> | null,
  next: Record<string, unknown>,
  dirty: boolean,
): Record<string, unknown> {
  if (!dirty || !current) return { ...next };
  const merged = { ...next };
  for (const key of [
    "displayName",
    "publicListingEnabled",
    "modSyncEnabled",
    "bandwidthCapacityMbps",
    "announcements",
    "chatFeatures",
  ]) {
    if (Object.hasOwn(current, key)) merged[key] = current[key];
  }
  return merged;
}

export function resolveServerAdminChatControlState(features: Record<string, unknown> | null) {
  const richContentEnabled = features?.richContentEnabled === true;
  const roomChatV2Enabled = features?.roomChatV2Enabled === true;
  return {
    serverChatEnabled: false,
    richContentEnabled: false,
    emojiEnabled: !richContentEnabled,
    itemRefsEnabled: !richContentEnabled,
    roomChatV2Enabled: false,
    roomCombatRefsEnabled: !richContentEnabled || !roomChatV2Enabled,
  };
}

export function calculateServerAdminRejectionRate(accepted: unknown, rejected: unknown): number {
  const acceptedCount = typeof accepted === "number" && accepted >= 0 ? accepted : 0;
  const rejectedCount = typeof rejected === "number" && rejected >= 0 ? rejected : 0;
  const total = acceptedCount + rejectedCount;
  return total === 0 ? 0 : rejectedCount / total;
}

export function beginServerAdminSingleFlight(ref: { current: boolean }): boolean {
  if (ref.current) return false;
  ref.current = true;
  return true;
}

export function releaseServerAdminSingleFlight(ref: { current: boolean }): void {
  ref.current = false;
}

export function buildServerAdminAiConfigPatch(
  values: Record<string, unknown>,
  apiKeyDraft: string,
): Record<string, unknown> {
  const patch: Record<string, unknown> = {
    enabled: values.enabled === true,
    protocol: typeof values.protocol === "string" && values.protocol
      ? values.protocol
      : "openai_responses",
    endpoint: typeof values.endpoint === "string" ? values.endpoint.trim() : "",
    model: typeof values.model === "string" ? values.model.trim() : "",
    allowPrivateNetwork: values.allowPrivateNetwork === true,
    jsonFallbackEnabled: values.jsonFallbackEnabled === true,
    apiKeyAction: "keep",
  };
  const draft = typeof apiKeyDraft === "string" ? apiKeyDraft.trim() : "";
  if (draft) {
    patch.apiKeyAction = "replace";
    patch.apiKey = draft;
  }
  return patch;
}

export function buildServerAdminAiTestRequest(
  values: Record<string, unknown>,
  apiKeyDraft: string,
): Record<string, unknown> {
  const body: Record<string, unknown> = {
    protocol: typeof values.protocol === "string" && values.protocol
      ? values.protocol
      : "openai_responses",
    endpoint: typeof values.endpoint === "string" ? values.endpoint.trim() : "",
    model: typeof values.model === "string" ? values.model.trim() : "",
    allowPrivateNetwork: values.allowPrivateNetwork === true,
    jsonFallbackEnabled: values.jsonFallbackEnabled === true,
  };
  const draft = typeof apiKeyDraft === "string" ? apiKeyDraft.trim() : "";
  if (draft) {
    body.apiKey = draft;
  }
  return body;
}

export function resolveServerAdminAiCredentialAlert(
  config: Record<string, unknown> | null,
): { type: string; message: string; description: string } | null {
  const status = config && typeof config.credentialStatus === "string"
    ? config.credentialStatus
    : "missing_api_key";
  switch (status) {
    case "ready":
      return null;
    case "missing_master_key":
      return {
        type: "warning",
        message: "未配置 AI 审核主密钥",
        description: "运维需要先在环境变量中配置有效的 AI_MODERATION_CREDENTIAL_KEY，否则无法保存 API Key 或启用 AI 审核。",
      };
    case "decrypt_error":
      return {
        type: "error",
        message: "已保存的 API Key 无法解密",
        description: "主密钥可能已更换。请重新填写 API Key 并保存；旧的加密证据与缓存需要运维手动清理。",
      };
    default:
      return {
        type: "info",
        message: "尚未保存 API Key",
        description: "填写 API Key 并保存后才能启用 AI 语义审核。",
      };
  }
}

export function isCurrentServerAdminRequest(
  generation: number,
  currentGeneration: number,
  sequence?: number,
  currentSequence?: number,
): boolean {
  return generation === currentGeneration
    && (sequence === undefined || sequence === currentSequence);
}

export function buildServerAdminRequestInit(
  method: string | undefined,
  csrfToken: string | null,
  options: Record<string, unknown> = {},
): Record<string, unknown> {
  const normalizedMethod = (method || "GET").toUpperCase();
  const headers = {
    ...((options.headers && typeof options.headers === "object")
      ? options.headers as Record<string, string>
      : {}),
  };
  if (!["GET", "HEAD", "OPTIONS"].includes(normalizedMethod) && csrfToken) {
    headers["x-csrf-token"] = csrfToken;
  }
  return {
    ...options,
    method: normalizedMethod,
    credentials: "same-origin",
    headers,
  };
}

export function renderServerAdminPage(serviceVersion: string) {
  const versionLabel = `Lobby Service v${serviceVersion}`;
  return `<!doctype html>
<html lang="zh-CN">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="theme-color" content="#090f0d" />
    <title>STS2 服务器控制台</title>
    <link rel="stylesheet" href="https://unpkg.com/antd@5.22.6/dist/reset.css" />
    <style>
      html, body, #root {
        min-height: 100%;
        -webkit-text-size-adjust: 100%;
        text-size-adjust: 100%;
      }
      body {
        margin: 0;
        background:
          radial-gradient(1100px 480px at 88% -12%, rgba(47, 212, 164, 0.08), transparent 60%),
          radial-gradient(820px 400px at -12% -6%, rgba(92, 179, 230, 0.06), transparent 55%),
          #090f0d;
        color: #e8f0ea;
        font-family: "PingFang SC", "Microsoft YaHei", sans-serif;
      }
      .page-shell {
        min-height: 100vh;
        background: transparent;
      }
      .page-header {
        position: sticky;
        top: 0;
        z-index: 8;
        height: auto;
        min-height: 0;
        min-width: 0;
        line-height: normal;
        padding: 0;
        background: rgba(9, 15, 13, 0.85);
        backdrop-filter: blur(14px);
        -webkit-backdrop-filter: blur(14px);
        border-bottom: 1px solid #1c2a22;
      }
      .page-header-inner {
        max-width: 1360px;
        margin: 0 auto;
        padding: 16px 24px;
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: 14px;
      }
      .page-brand {
        display: flex;
        align-items: center;
        gap: 12px;
        min-width: 0;
        flex: 1 1 auto;
      }
      .brand-mark {
        width: 40px;
        height: 40px;
        flex: 0 0 auto;
        border-radius: 11px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: linear-gradient(135deg, #123b30, #0d1f1a);
        border: 1px solid rgba(47, 212, 164, 0.4);
        box-shadow: 0 0 18px rgba(47, 212, 164, 0.15);
        color: #2fd4a4;
      }
      .brand-mark .svg-icon {
        width: 20px;
        height: 20px;
      }
      .page-brand-text {
        display: flex;
        flex-direction: column;
        gap: 3px;
        min-width: 0;
      }
      .page-brand-title {
        margin: 0 !important;
        line-height: 1.15;
        overflow-wrap: anywhere;
        color: #eef5f0 !important;
      }
      .page-brand-subtitle {
        line-height: 1.45;
        overflow-wrap: anywhere;
        color: #7f948a !important;
        font-size: 12px;
      }
      .version-chip {
        display: inline-flex;
        align-items: center;
        align-self: flex-start;
        max-width: 100%;
        padding: 1px 9px;
        border-radius: 999px;
        border: 1px solid #2a3d33;
        background: #131e19;
        color: #8ea398;
        font-size: 11px;
        line-height: 1.6;
        font-variant-numeric: tabular-nums;
        overflow-wrap: anywhere;
      }
      .page-actions {
        display: flex;
        align-items: center;
        justify-content: flex-end;
        flex-wrap: wrap;
        gap: 12px;
        flex: 0 0 auto;
        margin-left: auto;
      }
      .page-content {
        width: 100%;
        max-width: 1360px;
        min-width: 0;
        margin: 0 auto;
        padding: 24px 24px 12px;
      }
      .page-footer {
        width: 100%;
        max-width: 1360px;
        min-width: 0;
        margin: 4px auto 0;
        padding: 16px 24px 28px;
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: 10px 16px;
        border-top: 1px solid #14201a;
        color: #64766d;
        font-size: 12px;
      }
      .footer-group {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 8px 14px;
        min-width: 0;
      }
      .footer-dot {
        width: 7px;
        height: 7px;
        border-radius: 50%;
        background: #2fd4a4;
        box-shadow: 0 0 8px rgba(47, 212, 164, 0.6);
        flex: 0 0 auto;
      }
      .login-shell {
        min-height: 100vh;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 24px;
      }
      .login-wrap {
        width: 100%;
        max-width: 400px;
      }
      .login-card,
      .console-card {
        border-radius: 14px;
        box-shadow: 0 12px 32px rgba(0, 0, 0, 0.35);
        border-color: #1c2a22;
        min-width: 0;
      }
      .login-brand {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 10px;
        margin-bottom: 18px;
        text-align: center;
      }
      .login-brand .brand-mark {
        width: 52px;
        height: 52px;
        border-radius: 14px;
      }
      .login-brand .brand-mark .svg-icon {
        width: 26px;
        height: 26px;
      }
      .page-actions .ant-badge-status-text {
        color: #c6d6cd;
      }
      .page-actions .ant-btn {
        color: #d7e4dc;
        border-color: #33463c;
        background: transparent;
      }
      .console-card .ant-card-head {
        border-bottom-color: #1c2a22;
      }
      .dashboard-stack {
        display: grid;
        gap: 16px;
      }
      .update-panel {
        border: 1px solid rgba(47, 212, 164, 0.35);
        background: linear-gradient(120deg, rgba(47, 212, 164, 0.10), rgba(16, 25, 20, 0) 42%), #101914;
      }
      .update-panel.available {
        border-color: rgba(242, 176, 77, 0.5);
        background: linear-gradient(120deg, rgba(242, 176, 77, 0.12), rgba(16, 25, 20, 0) 45%), #101914;
      }
      .update-layout {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        align-items: center;
        gap: 20px;
      }
      .version-line {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: 10px;
      }
      .version-number {
        font-size: 30px;
        font-weight: 700;
        line-height: 1;
        color: #eef5f0;
        font-variant-numeric: tabular-nums;
      }
      .metric-grid {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        gap: 14px;
      }
      .metric-tile {
        min-height: 128px;
        padding: 16px 18px;
        border: 1px solid #1c2a22;
        border-radius: 14px;
        background: linear-gradient(180deg, #131e19, #101914);
        display: flex;
        flex-direction: column;
        gap: 6px;
        justify-content: flex-start;
      }
      .metric-tile.tone-good { border-top: 2px solid #2fd4a4; }
      .metric-tile.tone-warn { border-top: 2px solid #f2b04d; }
      .metric-tile.tone-info { border-top: 2px solid #5cb3e6; }
      .metric-label {
        color: #8ea398;
        font-size: 12px;
        letter-spacing: 0.04em;
      }
      .metric-label-icon {
        display: flex;
        align-items: center;
        gap: 6px;
      }
      .metric-label-icon .svg-icon {
        width: 13px;
        height: 13px;
        color: #5f7569;
      }
      .metric-value {
        color: #eef5f0;
        font-size: 25px;
        font-weight: 700;
        line-height: 1.15;
        overflow-wrap: anywhere;
        font-variant-numeric: tabular-nums;
      }
      .metric-main {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 10px;
      }
      .metric-note {
        color: #64766d;
        font-size: 12px;
      }
      .sparkline {
        width: 100%;
        height: 36px;
        display: block;
        margin: 2px 0;
      }
      .sparkline-line {
        fill: none;
        stroke: #2fd4a4;
        stroke-width: 1.6;
        stroke-linecap: round;
        stroke-linejoin: round;
        vector-effect: non-scaling-stroke;
      }
      .sparkline-area {
        fill: rgba(47, 212, 164, 0.12);
        stroke: none;
      }
      .sparkline-empty {
        display: flex;
        align-items: center;
        color: #64766d;
        font-size: 12px;
      }
      .usage-ring {
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        flex: 0 0 auto;
      }
      .usage-ring-inner {
        width: 74%;
        height: 74%;
        border-radius: 50%;
        background: #101914;
        display: flex;
        align-items: center;
        justify-content: center;
        font-weight: 700;
        color: #eef5f0;
        font-variant-numeric: tabular-nums;
      }
      .meter {
        height: 8px;
        border-radius: 999px;
        background: rgba(255, 255, 255, 0.06);
        overflow: hidden;
        margin: 4px 0 12px;
      }
      .meter-fill {
        height: 100%;
        border-radius: 999px;
        transition: width 0.45s ease;
      }
      .meter-legend {
        display: flex;
        flex-wrap: wrap;
        gap: 6px 16px;
        margin-top: 8px;
        font-size: 12px;
        color: #8ea398;
      }
      .legend-dot {
        display: inline-block;
        width: 8px;
        height: 8px;
        border-radius: 50%;
        margin-right: 6px;
      }
      .legend-empty {
        color: #64766d;
      }
      .quality-meter {
        margin-bottom: 12px;
      }
      .quality-meter-head {
        display: flex;
        justify-content: space-between;
        gap: 12px;
        font-size: 12px;
        color: #8ea398;
        margin-bottom: 6px;
      }
      .quality-meter-value {
        color: #dce8e0;
        font-weight: 600;
        font-variant-numeric: tabular-nums;
      }
      .detail-panels {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 14px;
      }
      .detail-panel {
        padding: 18px;
        border: 1px solid #1c2a22;
        border-radius: 14px;
        background: linear-gradient(180deg, #121c17, #0f1713);
      }
      .detail-panel-title {
        margin: 0 0 12px;
        font-size: 14px;
        font-weight: 700;
        color: #dce8e0;
        display: flex;
        align-items: center;
        gap: 8px;
      }
      .detail-panel-title .svg-icon {
        width: 15px;
        height: 15px;
        color: #2fd4a4;
        flex: 0 0 auto;
      }
      .detail-row {
        min-height: 36px;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
        border-top: 1px solid #18241e;
        font-size: 13px;
      }
      .detail-row-label { color: #7f948a; }
      .detail-row-value {
        color: #e8f0ea;
        font-weight: 600;
        text-align: right;
        overflow-wrap: anywhere;
        font-variant-numeric: tabular-nums;
      }
      .toggle-button {
        min-width: 104px;
        border-color: #33463c !important;
        background: #18231e !important;
        color: #9fb3a8 !important;
      }
      .toggle-button.active {
        border-color: #2fd4a4 !important;
        background: linear-gradient(135deg, #1d5c48, #155040) !important;
        color: #eafff7 !important;
      }
      .feature-grid .ant-form-item {
        margin-bottom: 14px;
        padding: 12px;
        border: 1px solid #1c2a22;
        border-radius: 10px;
        background: #131e19;
      }
      .console-card .ant-card-head-wrapper,
      .console-card .ant-card-head-title,
      .console-card .ant-card-extra {
        min-width: 0;
      }
      .section-title {
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: 12px;
      }
      .announcement-list {
        display: grid;
        gap: 14px;
      }
      .announcement-card {
        border: 1px solid #1c2a22;
        border-radius: 14px;
        padding: 16px;
        background: #131e19;
      }
      .announcement-card-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 12px;
        margin-bottom: 16px;
      }
      .announcement-grid {
        display: grid;
        gap: 12px;
      }
      .announcement-grid.two-columns {
        grid-template-columns: minmax(0, 1fr) minmax(160px, 220px);
      }
      .announcement-toolbar {
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
      }
      .settings-actions,
      .announcement-actions {
        max-width: 100%;
      }
      .announcement-empty {
        padding: 18px 20px;
        border-radius: 14px;
        background: #131e19;
        border: 1px dashed #2a3d33;
      }
      .announcement-card .ant-input,
      .announcement-card .ant-input-affix-wrapper,
      .announcement-card .ant-select-selector,
      .announcement-card .ant-input-number {
        border-radius: 10px !important;
      }
      .announcement-card .ant-input-textarea textarea {
        min-height: 112px;
      }
      .feature-grid {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        gap: 4px 16px;
      }
      .console-nav {
        display: flex;
        gap: 6px;
        margin-bottom: 20px;
        padding: 6px;
        border: 1px solid #1c2a22;
        border-radius: 14px;
        background: #0d1512;
        overflow-x: auto;
        -webkit-overflow-scrolling: touch;
        scrollbar-width: none;
      }
      .console-nav::-webkit-scrollbar {
        display: none;
      }
      .console-nav-btn {
        flex: 1 0 auto;
        min-width: max-content;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: 8px;
        padding: 10px 18px;
        border-radius: 10px;
        border: 1px solid transparent;
        background: transparent;
        color: #8ea398;
        font-size: 14px;
        font-family: inherit;
        cursor: pointer;
        transition: background 0.2s ease, color 0.2s ease, border-color 0.2s ease;
      }
      .console-nav-btn:hover {
        color: #d7e4dc;
        background: #131e19;
      }
      .console-nav-btn.active {
        background: #15231d;
        color: #eafff7;
        border-color: rgba(47, 212, 164, 0.4);
      }
      .console-nav-btn .svg-icon {
        width: 15px;
        height: 15px;
      }
      .console-nav-btn.active .svg-icon {
        color: #2fd4a4;
      }
      .nav-dot {
        width: 7px;
        height: 7px;
        border-radius: 50%;
        flex: 0 0 auto;
      }
      .nav-count {
        min-width: 20px;
        padding: 0 6px;
        border-radius: 999px;
        background: #1a2620;
        border: 1px solid #2a3d33;
        font-size: 11px;
        line-height: 1.6;
        color: #8ea398;
        text-align: center;
        font-variant-numeric: tabular-nums;
      }
      @keyframes consoleFadeUp {
        from { opacity: 0; transform: translateY(6px); }
        to { opacity: 1; transform: none; }
      }
      .tab-panel,
      .dashboard-stack {
        animation: consoleFadeUp 0.25s ease;
      }
      .tab-narrow {
        max-width: 860px;
        margin: 0 auto;
        width: 100%;
      }
      .update-teaser {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
        width: 100%;
        padding: 12px 16px;
        border-radius: 12px;
        border: 1px solid rgba(242, 176, 77, 0.5);
        background: linear-gradient(120deg, rgba(242, 176, 77, 0.14), rgba(242, 176, 77, 0.04));
        color: #f5c877;
        font-size: 13px;
        font-family: inherit;
        cursor: pointer;
        text-align: left;
        transition: background 0.2s ease;
      }
      .update-teaser:hover {
        background: linear-gradient(120deg, rgba(242, 176, 77, 0.2), rgba(242, 176, 77, 0.07));
      }
      .update-teaser-arrow {
        flex: 0 0 auto;
        font-size: 15px;
      }
      .panel-visual-row {
        display: flex;
        align-items: center;
        gap: 18px;
        flex-wrap: wrap;
        margin-bottom: 12px;
      }
      .panel-visual-main {
        flex: 1 1 180px;
        min-width: 0;
      }
      .panel-visual-value {
        font-size: 16px;
        font-weight: 700;
        color: #eef5f0;
        margin-bottom: 4px;
        font-variant-numeric: tabular-nums;
      }
      .panel-visual-note {
        font-size: 12px;
        color: #64766d;
      }
      .fact-chips {
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
        margin: 2px 0 12px;
      }
      .fact-chip {
        display: flex;
        flex-direction: column;
        gap: 3px;
        padding: 8px 12px;
        border: 1px solid #1c2a22;
        border-radius: 10px;
        background: #0d1512;
        min-width: 0;
        flex: 1 1 120px;
      }
      .fact-chip-label {
        font-size: 11px;
        color: #64766d;
      }
      .fact-chip-value {
        font-size: 13px;
        font-weight: 600;
        color: #e8f0ea;
        font-variant-numeric: tabular-nums;
        overflow-wrap: anywhere;
      }
      .relay-donut {
        display: flex;
        align-items: center;
        gap: 18px;
        flex-wrap: wrap;
        margin-bottom: 14px;
      }
      .relay-donut-svg {
        width: 96px;
        height: 96px;
        flex: 0 0 auto;
      }
      .donut-track {
        fill: none;
        stroke: rgba(255, 255, 255, 0.07);
        stroke-width: 11;
      }
      .donut-host {
        fill: none;
        stroke: #2fd4a4;
        stroke-width: 11;
        stroke-linecap: round;
      }
      .donut-client {
        fill: none;
        stroke: #5cb3e6;
        stroke-width: 11;
        stroke-linecap: round;
      }
      .donut-center-value {
        fill: #eef5f0;
        font-size: 24px;
        font-weight: 700;
        text-anchor: middle;
      }
      .donut-center-label {
        fill: #64766d;
        font-size: 10px;
        text-anchor: middle;
      }
      .relay-donut-legend {
        display: flex;
        flex-wrap: wrap;
        gap: 6px 16px;
        font-size: 12px;
        color: #8ea398;
      }
      .release-notes {
        white-space: pre-wrap;
        overflow-wrap: anywhere;
        background: #0d1512;
        border: 1px solid #1c2a22;
        padding: 12px 14px;
        border-radius: 10px;
        font-size: 12px;
        line-height: 1.7;
        color: #8ea398;
        max-height: 280px;
        overflow-y: auto;
      }
      .mod-list {
        display: grid;
        gap: 10px;
      }
      .mod-item {
        border: 1px solid #1c2a22;
        border-radius: 10px;
        padding: 12px 14px;
        background: #131e19;
        display: grid;
        gap: 8px;
        min-width: 0;
      }
      .mod-item-head {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 10px;
        flex-wrap: wrap;
      }
      .mod-item-title {
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
        min-width: 0;
        font-weight: 600;
        color: #e8f0ea;
        overflow-wrap: anywhere;
      }
      .mod-item-actions {
        display: flex;
        gap: 8px;
        flex-wrap: wrap;
        flex: 0 0 auto;
      }
      .mod-item-meta {
        display: flex;
        flex-wrap: wrap;
        gap: 4px 14px;
        font-size: 12px;
        color: #8ea398;
        overflow-wrap: anywhere;
      }
      .mod-error-list {
        display: grid;
        gap: 6px;
      }
      .mod-error-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 12px;
        flex-wrap: wrap;
        border: 1px solid #1c2a22;
        border-radius: 8px;
        padding: 8px 12px;
        background: #0d1512;
        font-size: 12px;
      }
      .mod-error-code {
        color: #e8f0ea;
        font-weight: 600;
        overflow-wrap: anywhere;
      }
      .mod-error-time {
        color: #64766d;
        font-variant-numeric: tabular-nums;
        flex: 0 0 auto;
      }
      .mod-pager {
        display: flex;
        justify-content: flex-end;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
      }
      .mod-evidence-label {
        font-size: 12px;
        color: #64766d;
        margin-bottom: 4px;
      }
      .mod-detail-grid {
        display: grid;
        gap: 2px;
      }
      @media (max-width: 640px) {
        .mod-item-head {
          align-items: flex-start;
        }
        .mod-item-actions {
          width: 100%;
        }
        .mod-pager {
          justify-content: stretch;
        }
        .mod-pager .ant-btn {
          flex: 1 1 auto;
        }
      }
      @media (prefers-reduced-motion: reduce) {
        .tab-panel,
        .dashboard-stack {
          animation: none;
        }
      }
      @media (max-width: 992px) {
        .page-header-inner,
        .page-content {
          padding-left: 16px;
          padding-right: 16px;
        }
        .announcement-grid.two-columns {
          grid-template-columns: 1fr;
        }
        .feature-grid {
          grid-template-columns: 1fr;
        }
        .metric-grid {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }
      }
      @media (max-width: 640px) {
        .page-header-inner {
          flex-direction: column;
          align-items: stretch;
          padding-top: 14px;
          padding-bottom: 14px;
          gap: 12px;
        }
        .page-brand-title {
          font-size: 22px !important;
        }
        .page-brand-subtitle {
          font-size: 12px;
        }
        .page-actions {
          width: 100%;
        }
        .console-card .ant-card-head {
          min-height: 0;
          padding-inline: 16px;
        }
        .console-card .ant-card-body {
          padding: 16px;
        }
        .console-card .ant-card-head-wrapper {
          align-items: flex-start;
          flex-wrap: wrap;
          gap: 8px;
        }
        .console-card .ant-card-head-title {
          white-space: normal;
          overflow-wrap: anywhere;
        }
        .console-card .ant-card-extra {
          margin-inline-start: 0;
          max-width: 100%;
        }
        .settings-actions,
        .announcement-actions {
          display: flex !important;
          flex-wrap: wrap;
          width: 100%;
          row-gap: 8px;
        }
        .settings-actions .ant-space-item,
        .announcement-actions .ant-space-item {
          min-width: 0;
          max-width: 100%;
        }
        .settings-actions .ant-btn,
        .announcement-actions .ant-btn {
          max-width: 100%;
          white-space: normal;
          height: auto;
          min-height: 32px;
        }
        .section-title,
        .announcement-card-header {
          flex-wrap: wrap;
        }
        .update-layout {
          grid-template-columns: 1fr;
        }
        .update-layout .ant-space {
          width: 100%;
        }
        .update-layout .ant-btn {
          flex: 1 1 auto;
        }
        .metric-grid,
        .detail-panels {
          grid-template-columns: 1fr;
        }
        .metric-tile {
          min-height: 104px;
        }
        .metric-value {
          font-size: 22px;
        }
        .version-number {
          font-size: 24px;
        }
        .toggle-button {
          width: 100%;
        }
        .console-nav {
          margin-bottom: 16px;
        }
        .console-nav-btn {
          padding: 9px 14px;
          font-size: 13px;
        }
        .panel-visual-row {
          gap: 14px;
        }
      }
      @media (max-width: 480px) {
        .page-content {
          padding-top: 16px;
          padding-bottom: 16px;
        }
        .page-brand-subtitle {
          display: none;
        }
        .page-footer {
          flex-direction: column;
          align-items: flex-start;
          padding-bottom: 22px;
        }
        .page-actions {
          justify-content: space-between;
        }
        .page-actions .ant-badge-status-text {
          font-size: 12px;
        }
      }
    </style>
  </head>
  <body>
    <div id="root"></div>
    <script crossorigin src="https://unpkg.com/dayjs@1.11.13/dayjs.min.js"></script>
    <script crossorigin src="https://unpkg.com/react@18/umd/react.production.min.js"></script>
    <script crossorigin src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"></script>
    <script crossorigin src="https://unpkg.com/antd@5.22.6/dist/antd.min.js"></script>
    <script>
      (function () {
        const h = React.createElement;
        const {
          Alert,
          Badge,
          Button,
          Card,
          Col,
          ConfigProvider,
          Descriptions,
          Form,
          Input,
          InputNumber,
          Layout,
          Modal,
          Radio,
          Row,
          Select,
          Space,
          Spin,
          Switch,
          Tag,
          Typography,
          message,
          notification,
        } = antd;
        const { Title, Paragraph, Text } = Typography;
        const TextArea = Input.TextArea;
        const serviceVersionLabel = ${JSON.stringify(versionLabel)};
        const mergePollSnapshot = ${mergeServerAdminPollSnapshot.toString()};
        const resolveChatControlState = ${resolveServerAdminChatControlState.toString()};
        const calculateRejectionRate = ${calculateServerAdminRejectionRate.toString()};
        const beginSingleFlight = ${beginServerAdminSingleFlight.toString()};
        const releaseSingleFlight = ${releaseServerAdminSingleFlight.toString()};
        const isCurrentRequest = ${isCurrentServerAdminRequest.toString()};
        const buildRequestInit = ${buildServerAdminRequestInit.toString()};
        const buildAiConfigPatch = ${buildServerAdminAiConfigPatch.toString()};
        const buildAiTestRequest = ${buildServerAdminAiTestRequest.toString()};
        const resolveAiCredentialAlert = ${resolveServerAdminAiCredentialAlert.toString()};

        const ANNOUNCEMENT_TYPES = [
          { value: "update", label: "更新" },
          { value: "event", label: "活动" },
          { value: "warning", label: "警告" },
          { value: "info", label: "信息" },
        ];

        const ADMIN_THEME = {
          algorithm: antd.theme.darkAlgorithm,
          token: {
            colorPrimary: "#2fd4a4",
            colorInfo: "#2fd4a4",
            colorBgContainer: "#101914",
            colorBgElevated: "#182219",
            colorBorder: "#233329",
            colorBorderSecondary: "#1c2a22",
            colorText: "#e8f0ea",
            colorTextSecondary: "#8ea398",
            borderRadius: 10,
            fontFamily: "'PingFang SC','Microsoft YaHei',sans-serif",
          },
        };

        const ICONS = {
          activity: '<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>',
          gauge: '<path d="m12 14 4-4"/><path d="M3.34 19a10 10 0 1 1 17.32 0"/>',
          network: '<rect x="16" y="16" width="6" height="6" rx="1"/><rect x="2" y="16" width="6" height="6" rx="1"/><rect x="9" y="2" width="6" height="6" rx="1"/><path d="M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3"/><path d="M12 12V8"/>',
          chat: '<path d="M7.9 20A9 9 0 1 0 4 16.1L2 22Z"/>',
          target: '<circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="6"/><circle cx="12" cy="12" r="2"/>',
          server: '<rect x="2" y="2" width="20" height="8" rx="2"/><rect x="2" y="14" width="20" height="8" rx="2"/><line x1="6" y1="6" x2="6.01" y2="6"/><line x1="6" y1="18" x2="6.01" y2="18"/>',
          dashboard: '<rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/>',
          sliders: '<line x1="4" y1="21" x2="4" y2="14"/><line x1="4" y1="10" x2="4" y2="3"/><line x1="12" y1="21" x2="12" y2="12"/><line x1="12" y1="8" x2="12" y2="3"/><line x1="20" y1="21" x2="20" y2="16"/><line x1="20" y1="12" x2="20" y2="3"/><line x1="1" y1="14" x2="7" y2="14"/><line x1="9" y1="8" x2="15" y2="8"/><line x1="17" y1="16" x2="23" y2="16"/>',
          megaphone: '<path d="m3 11 18-5v12L3 14v-3z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/>',
          download: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>',
          shield: '<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/>',
          ban: '<circle cx="12" cy="12" r="10"/><path d="m4.9 4.9 14.2 14.2"/>',
        };

        function svgIcon(inner) {
          return h("svg", {
            className: "svg-icon",
            viewBox: "0 0 24 24",
            fill: "none",
            stroke: "currentColor",
            strokeWidth: 2,
            strokeLinecap: "round",
            strokeLinejoin: "round",
            "aria-hidden": "true",
            focusable: "false",
            dangerouslySetInnerHTML: { __html: inner },
          });
        }

        class AdminRequestError extends Error {
          constructor(status, code, messageText) {
            super(messageText || "请求失败");
            this.name = "AdminRequestError";
            this.status = status;
            this.code = code || "request_failed";
          }
        }

        async function performAdminRequest(path, options, csrfToken) {
          const requestOptions = options || {};
          const response = await fetch(
            path,
            buildRequestInit(requestOptions.method, csrfToken, requestOptions),
          );
          const text = await response.text();
          let payload = {};
          if (text) {
            try {
              payload = JSON.parse(text);
            } catch {
              payload = {};
            }
          }
          if (!response.ok) {
            throw new AdminRequestError(
              response.status,
              payload.code,
              payload.message || response.statusText || "请求失败",
            );
          }
          return response.status === 204 ? null : payload;
        }

        function normalizeChatFeatures(value) {
          const next = value || {};
          return {
            serverChatEnabled: next.serverChatEnabled === true,
            richContentEnabled: next.richContentEnabled !== false,
            emojiEnabled: next.emojiEnabled !== false,
            itemRefsEnabled: next.itemRefsEnabled !== false,
            roomChatV2Enabled: next.roomChatV2Enabled !== false,
            roomCombatRefsEnabled: next.roomCombatRefsEnabled !== false,
          };
        }

        function sensitiveFilterExtra(filter) {
          if (!filter) {
            return "默认开启。聊天内容命中敏感词时自动以 * 替代；用户名、房间名等名称类字段命中时会被拒绝。";
          }
          if (filter.loadError) {
            return \`词库不可用（\${filter.loadError}），过滤当前不生效，请检查服务端 lexicon 目录。\`;
          }
          return \`词库 \${filter.wordCount} 词 · 已打码 \${filter.maskedMessages} 条消息 · 已拒绝 \${filter.rejectedNames} 个名称\`;
        }

        function formatDateTime(value) {
          if (!value) {
            return "未记录";
          }

          try {
            return new Date(value).toLocaleString();
          } catch {
            return String(value);
          }
        }

        function getListingStateMeta(settings) {
          if (!settings) {
            return {
              tagColor: "default",
              tagLabel: "读取中",
              alertType: "info",
              description: "正在读取节点网络运行状态。",
            };
          }

          switch (settings.peerRuntimeState) {
            case "disabled":
              return {
                tagColor: "default",
                tagLabel: "节点网络未启用",
                alertType: "info",
                description: "当前服务未启用去中心化节点网络，因此不会加入公共服务器列表。",
              };
            case "unconfigured":
              return {
                tagColor: "warning",
                tagLabel: "节点网络未配置",
                alertType: "warning",
                description: "已启用节点网络，但尚未配置本机对外地址，因此暂时无法加入节点网络。",
              };
            case "private":
              return {
                tagColor: "default",
                tagLabel: "仅私有可见",
                alertType: "info",
                description: "节点网络已启用，但当前服务器未公开到公共列表；知道直连地址的玩家仍可连接。",
              };
            case "joining":
              return {
                tagColor: "processing",
                tagLabel: "正在加入节点网络",
                alertType: "info",
                description: "当前服务器已开始公开，但还没有观察到外部活跃节点，可能仍在等待网络同步。",
              };
            case "joined":
              return {
                tagColor: "success",
                tagLabel: "已加入节点网络",
                alertType: "success",
                description: "当前服务器已观察到外部活跃节点，说明它已加入去中心化节点网络并可被公共列表传播。",
              };
            default:
              return {
                tagColor: "default",
                tagLabel: "读取中",
                alertType: "info",
                description: "正在读取节点网络运行状态。",
              };
          }
        }

        function renderListingStateTag(settings) {
          const meta = getListingStateMeta(settings);
          return h(Tag, { color: meta.tagColor }, meta.tagLabel);
        }

        function buildStatusAlert(settings) {
          const meta = getListingStateMeta(settings);
          return {
            type: meta.alertType,
            message: "节点网络：" + meta.tagLabel,
            description: meta.description,
          };
        }

        function notifySyncState(_next, _previous, _source) {
          // Decentralized network — no central审核流程, nothing to notify about
          // beyond what the form already shows. Kept as a no-op so existing
          // callsites compile.
        }

        function renderGuardTag(value, applies) {
          if (!applies) {
            return h(Tag, null, "未启用");
          }

          const mapping = {
            allow: ["success", "允许创建"],
            block: ["error", "禁止新建"],
            unknown: ["warning", "状态未知"],
          };
          const item = mapping[value] || ["default", value || "未知"];
          return h(Tag, { color: item[0] }, item[1]);
        }

        function renderCapacitySource(value) {
          const mapping = {
            manual: ["blue", "手动配置"],
            probe_peak_7d: ["gold", "近7天探针峰值"],
            unknown: ["default", "未知"],
          };
          const item = mapping[value] || ["default", value || "未知"];
          return h(Tag, { color: item[0] }, item[1]);
        }

        function formatMbps(value) {
          if (typeof value !== "number") {
            return "未设置";
          }

          if (value <= 0) {
            return "0.00 Mbps";
          }

          if (value < 0.01) {
            return "< 0.01 Mbps";
          }

          return value.toFixed(2) + " Mbps";
        }

        function formatRatio(value) {
          return typeof value === "number" ? (value * 100).toFixed(1) + "%" : "未计算";
        }

        function formatBytes(value) {
          if (typeof value !== "number" || value < 0) {
            return "未记录";
          }

          if (value < 1024) {
            return value.toFixed(0) + " B";
          }

          if (value < 1024 * 1024) {
            return (value / 1024).toFixed(1) + " KB";
          }

          if (value < 1024 * 1024 * 1024) {
            return (value / (1024 * 1024)).toFixed(2) + " MB";
          }

          return (value / (1024 * 1024 * 1024)).toFixed(2) + " GB";
        }

        function formatWindowSeconds(value) {
          return typeof value === "number" && value > 0 ? (value / 1000).toFixed(0) + " 秒" : "未记录";
        }

        function formatFeatureVersions(value) {
          const next = value || {};
          return [
            "富文本 v" + (next.richContentVersion || 0),
            "Emoji v" + (next.emojiSetVersion || 0),
            "物品引用 v" + (next.itemRefVersion || 0),
            "战斗引用 v" + (next.combatRefVersion || 0),
          ].join(" · ");
        }

        function formatMetricCount(value) {
          return typeof value === "number" && value >= 0 ? value : 0;
        }

        function formatRejectionRate(accepted, rejected) {
          return (calculateRejectionRate(accepted, rejected) * 100).toFixed(1) + "%";
        }

        function createAnnouncementDraft(partial) {
          const next = partial || {};
          return {
            id: typeof next.id === "string" && next.id ? next.id : createAnnouncementId(),
            type: next.type === "update" || next.type === "event" || next.type === "warning" || next.type === "info"
              ? next.type
              : "info",
            title: typeof next.title === "string" ? next.title : "",
            dateLabel: typeof next.dateLabel === "string" ? next.dateLabel : "",
            body: typeof next.body === "string" ? next.body : "",
            enabled: next.enabled !== false,
          };
        }

        function normalizeAnnouncements(value) {
          return Array.isArray(value) ? value.map(createAnnouncementDraft) : [];
        }

        function createAnnouncementId() {
          if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
            return crypto.randomUUID();
          }

          return "announcement-" + Date.now() + "-" + Math.random().toString(16).slice(2);
        }

        function ToggleButton(props) {
          const enabled = props.value === true;
          return h(Button, {
            className: "toggle-button" + (enabled ? " active" : ""),
            disabled: props.disabled,
            "aria-pressed": enabled,
            onClick: function () {
              if (!props.disabled && typeof props.onChange === "function") props.onChange(!enabled);
            },
          }, enabled ? (props.activeLabel || "已开启") : (props.inactiveLabel || "已关闭"));
        }

        function metricTile(label, value, note, tone, iconInner) {
          return h(
            "div",
            { className: "metric-tile tone-" + (tone || "info") },
            h(
              "div",
              { className: "metric-label" + (iconInner ? " metric-label-icon" : "") },
              iconInner ? svgIcon(iconInner) : null,
              label
            ),
            h("div", { className: "metric-value" }, value),
            h("div", { className: "metric-note" }, note)
          );
        }

        function detailRow(label, value) {
          return h(
            "div",
            { className: "detail-row" },
            h("span", { className: "detail-row-label" }, label),
            h("span", { className: "detail-row-value" }, value)
          );
        }

        function usageRing(ratio, size) {
          const pct = typeof ratio === "number" && ratio > 0
            ? Math.min(100, Math.round(ratio * 100))
            : 0;
          const color = pct > 80 ? "#ef6f6f" : pct > 60 ? "#f2b04d" : "#2fd4a4";
          const dimension = size || 52;
          return h(
            "div",
            {
              className: "usage-ring",
              role: "img",
              "aria-label": "容量利用率 " + pct + "%",
              style: {
                width: dimension,
                height: dimension,
                background: "conic-gradient(" + color + " " + pct + "%, rgba(255, 255, 255, 0.07) 0)",
              },
            },
            h(
              "div",
              { className: "usage-ring-inner", style: { fontSize: dimension >= 50 ? 13 : 11 } },
              pct + "%"
            )
          );
        }

        function utilizationMeter(ratio) {
          const pct = typeof ratio === "number" && ratio > 0
            ? Math.min(100, Math.round(ratio * 1000) / 10)
            : 0;
          const color = pct > 80 ? "#ef6f6f" : pct > 60 ? "#f2b04d" : "#2fd4a4";
          return h(
            "div",
            { className: "meter" },
            h("div", {
              className: "meter-fill",
              style: { width: pct + "%", background: "linear-gradient(90deg, " + color + "99, " + color + ")" },
            })
          );
        }

        function relayDonut(hosts, clients) {
          const hostCount = typeof hosts === "number" && hosts > 0 ? hosts : 0;
          const clientCount = typeof clients === "number" && clients > 0 ? clients : 0;
          const total = hostCount + clientCount;
          const radius = 42;
          const circumference = 2 * Math.PI * radius;
          const hostLength = total === 0 ? 0 : (hostCount / total) * circumference;
          const clientLength = total === 0 ? 0 : (clientCount / total) * circumference;
          return h(
            "div",
            { className: "relay-donut" },
            h(
              "svg",
              {
                className: "relay-donut-svg",
                viewBox: "0 0 120 120",
                role: "img",
                "aria-label": "活跃 relay 会话 " + total + " 个",
              },
              h("circle", { cx: 60, cy: 60, r: radius, className: "donut-track" }),
              hostLength > 0
                ? h("circle", {
                    cx: 60,
                    cy: 60,
                    r: radius,
                    className: "donut-host",
                    strokeDasharray: hostLength.toFixed(1) + " " + circumference.toFixed(1),
                    transform: "rotate(-90 60 60)",
                  })
                : null,
              clientLength > 0
                ? h("circle", {
                    cx: 60,
                    cy: 60,
                    r: radius,
                    className: "donut-client",
                    strokeDasharray: clientLength.toFixed(1) + " " + circumference.toFixed(1),
                    strokeDashoffset: (-hostLength).toFixed(1),
                    transform: "rotate(-90 60 60)",
                  })
                : null,
              h("text", { x: 60, y: 58, className: "donut-center-value" }, String(total)),
              h("text", { x: 60, y: 76, className: "donut-center-label" }, "活跃会话")
            ),
            h(
              "div",
              { className: "relay-donut-legend" },
              h("span", null, h("i", { className: "legend-dot", style: { background: "#2fd4a4" } }), "Host " + hostCount),
              h("span", null, h("i", { className: "legend-dot", style: { background: "#5cb3e6" } }), "Client " + clientCount),
              total === 0 ? h("span", { className: "legend-empty" }, "暂无活跃 relay 会话") : null
            )
          );
        }

        function factChip(label, value) {
          return h(
            "div",
            { className: "fact-chip" },
            h("span", { className: "fact-chip-label" }, label),
            h("span", { className: "fact-chip-value" }, value)
          );
        }

        function chatQualityMeter(label, accepted, rejected) {
          const ok = typeof accepted === "number" && accepted > 0 ? accepted : 0;
          const bad = typeof rejected === "number" && rejected > 0 ? rejected : 0;
          const total = ok + bad;
          const rate = total === 0 ? null : ok / total;
          const pct = rate === null ? 0 : Math.round(rate * 1000) / 10;
          const color = rate === null
            ? "rgba(255, 255, 255, 0.08)"
            : rate >= 0.95 ? "#2fd4a4" : rate >= 0.85 ? "#f2b04d" : "#ef6f6f";
          return h(
            "div",
            { className: "quality-meter" },
            h(
              "div",
              { className: "quality-meter-head" },
              h("span", null, label),
              h("span", { className: "quality-meter-value" }, rate === null ? "暂无消息" : pct + "% 接受")
            ),
            h(
              "div",
              { className: "meter" },
              h("div", { className: "meter-fill", style: { width: pct + "%", background: color } })
            )
          );
        }

        function Sparkline(props) {
          const samples = Array.isArray(props.points) ? props.points.slice(-40) : [];
          if (samples.length < 2) {
            return h("div", { className: "sparkline sparkline-empty" }, "等待流量采样…");
          }
          const width = 120;
          const height = 34;
          const max = Math.max.apply(null, samples.concat([0.01]));
          const step = width / (samples.length - 1);
          const coords = samples.map(function (value, index) {
            const x = index * step;
            const y = height - 3 - (Math.max(0, value) / max) * (height - 6);
            return [x, y];
          });
          const linePath = coords.map(function (point, index) {
            return (index === 0 ? "M" : "L") + point[0].toFixed(1) + " " + point[1].toFixed(1);
          }).join(" ");
          const areaPath = linePath + " L" + width + " " + height + " L0 " + height + " Z";
          return h(
            "svg",
            {
              className: "sparkline",
              viewBox: "0 0 " + width + " " + height,
              preserveAspectRatio: "none",
              "aria-hidden": "true",
            },
            h("path", { className: "sparkline-area", d: areaPath }),
            h("path", { className: "sparkline-line", d: linePath })
          );
        }

        function getUpdatePhaseMeta(update) {
          const phase = update && update.phase;
          const mapping = {
            idle: ["default", "等待检查"],
            checking: ["processing", "正在检查"],
            available: ["warning", "发现新版本"],
            downloading: ["processing", "正在下载"],
            installing: ["processing", "正在安装"],
            restarting: ["processing", "正在重启"],
            up_to_date: ["success", "已是最新版"],
            failed: ["error", "更新异常"],
          };
          return mapping[phase] || mapping.idle;
        }

        function deploymentLabel(mode) {
          if (mode === "docker") return "Docker";
          if (mode === "systemd") return "systemd";
          return "未识别";
        }

        const AI_PROTOCOL_OPTIONS = [
          { value: "openai_responses", label: "OpenAI Responses" },
          { value: "openai_chat_completions", label: "OpenAI Chat Completions" },
          { value: "anthropic_messages", label: "Anthropic Messages" },
        ];

        const REVIEW_STATUS_META = {
          pending: ["warning", "待复审"],
          approved: ["success", "已批准"],
          rejected: ["default", "已拒绝"],
        };

        const ALLOW_SCOPE_META = {
          term: ["processing", "整个词"],
          context: ["blue", "仅此语境"],
        };

        function reviewStatusTag(status) {
          const meta = REVIEW_STATUS_META[status] || ["default", status || "未知"];
          return h(Tag, { color: meta[0] }, meta[1]);
        }

        function allowScopeTag(scope) {
          const meta = ALLOW_SCOPE_META[scope] || ["default", scope || "未知"];
          return h(Tag, { color: meta[0] }, meta[1]);
        }

        function moderationSurfaceLabel(surface) {
          if (surface === "room_name") return "房间名";
          if (surface === "player_name") return "玩家名";
          if (surface === "character_name") return "角色名";
          return "聊天消息";
        }

        function circuitStateTag(health) {
          if (health.circuitState === "open") {
            return h(Tag, { color: "error" }, "熔断中" + (health.circuitOpenUntil ? " · 至 " + formatDateTime(health.circuitOpenUntil) : ""));
          }
          return h(Tag, { color: "success" }, "熔断关闭");
        }

        function structuredModeLabel(mode) {
          if (mode === "strict") return "strict（结构化）";
          if (mode === "prompted_json") return "prompted_json（提示词回退）";
          return mode || "未知";
        }

        function PermanentBlocklistPanel(props) {
          const request = props.request;
          const beginAdminMutation = props.beginAdminMutation;
          const endAdminMutation = props.endAdminMutation;
          const adminMutationInFlight = props.adminMutationInFlight;
          const refreshStatus = props.refreshStatus;

          const [scopeFilter, setScopeFilter] = React.useState("");
          const [items, setItems] = React.useState([]);
          const [cursor, setCursor] = React.useState(null);
          const [nextCursor, setNextCursor] = React.useState(null);
          const [cursorStack, setCursorStack] = React.useState([]);
          const [loading, setLoading] = React.useState(false);
          const [revokeLoadingId, setRevokeLoadingId] = React.useState(null);

          const loadBlocklist = React.useCallback(async function (nextPageCursor, stack, scope) {
            setLoading(true);
            try {
              const params = new URLSearchParams();
              const activeScope = scope === undefined ? scopeFilter : scope;
              if (activeScope) params.set("scope", activeScope);
              params.set("limit", "50");
              if (nextPageCursor) params.set("cursor", nextPageCursor);
              const data = await request("/server-admin/moderation/blocklist?" + params.toString());
              setItems(data && Array.isArray(data.items) ? data.items : []);
              setCursor(nextPageCursor || null);
              setNextCursor(data && data.nextCursor ? data.nextCursor : null);
              setCursorStack(stack || []);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "加载永久黑名单失败");
              }
            } finally {
              setLoading(false);
            }
          }, [request, scopeFilter]);

          React.useEffect(function () {
            void loadBlocklist(null, [], scopeFilter);
          }, [scopeFilter]);

          function confirmRevoke(item) {
            Modal.confirm({
              title: "撤销这条永久黑名单规则？",
              content: "撤销后立即失效，相同内容将重新按永久白名单、安全缓存或 AI 审核策略处理。",
              okText: "确认撤销",
              okButtonProps: { danger: true },
              cancelText: "取消",
              onOk: function () { return executeRevoke(item); },
            });
          }

          async function executeRevoke(item) {
            if (!item || !beginAdminMutation()) return;
            setRevokeLoadingId(item.id);
            try {
              await request("/server-admin/moderation/blocklist/" + encodeURIComponent(item.id), {
                method: "DELETE",
              });
              message.success("永久黑名单规则已撤销");
              await loadBlocklist(null, [], scopeFilter);
              if (refreshStatus) await refreshStatus({ source: "save" });
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "撤销永久黑名单规则失败");
              }
            } finally {
              endAdminMutation();
              setRevokeLoadingId(null);
            }
          }

          return h(
            "div",
            { className: "tab-panel", "data-admin-panel": "permanent-blocklist" },
            h(
              Space,
              { direction: "vertical", size: 16, style: { width: "100%" } },
              h(Alert, {
                type: "info",
                showIcon: true,
                message: "永久黑名单立即生效",
                description: "在 AI 审核页拒绝复审候选时会自动生成规则。整个词规则作用于聊天、房间名、玩家名和角色名；仅此语境规则只拦截相同信息类型、命中词和语境签名。",
              }),
              h(
                Card,
                {
                  className: "console-card",
                  title: h(
                    "div",
                    { className: "section-title" },
                    h("span", null, "永久黑名单"),
                    h(Tag, { color: "error" }, "当前页 " + items.length + " 条")
                  ),
                  extra: h(
                    Space,
                    { size: 8, wrap: true },
                    h(Select, {
                      value: scopeFilter,
                      style: { minWidth: 120 },
                      options: [
                        { value: "", label: "全部范围" },
                        { value: "term", label: "整个词" },
                        { value: "context", label: "仅此语境" },
                      ],
                      onChange: function (value) { setScopeFilter(value); },
                    }),
                    h(Button, {
                      onClick: function () { void loadBlocklist(null, [], scopeFilter); },
                      loading: loading,
                      disabled: adminMutationInFlight,
                    }, "刷新")
                  ),
                },
                loading && items.length === 0
                  ? h(Spin, { tip: "正在加载永久黑名单..." })
                  : items.length === 0
                    ? h(
                        "div",
                        { className: "announcement-empty" },
                        h(Text, { type: "secondary" }, "当前没有永久黑名单规则。请在 AI 审核页拒绝候选后再查看。")
                      )
                    : h(
                        "div",
                        { className: "mod-list" },
                        items.map(function (item) {
                          return h(
                            "div",
                            { key: item.id, className: "mod-item" },
                            h(
                              "div",
                              { className: "mod-item-head" },
                              h(
                                "div",
                                { className: "mod-item-title" },
                                h("span", null, item.normalizedTerm || "(未知词)"),
                                allowScopeTag(item.scope),
                                h(Tag, { color: "purple" }, moderationSurfaceLabel(item.surface))
                              ),
                              h(
                                "div",
                                { className: "mod-item-actions" },
                                h(Button, {
                                  size: "small",
                                  danger: true,
                                  loading: revokeLoadingId === item.id,
                                  disabled: adminMutationInFlight,
                                  onClick: function () { confirmRevoke(item); },
                                }, "撤销")
                              )
                            ),
                            h(
                              "div",
                              { className: "mod-item-meta" },
                              h("span", null, "规则 ID " + (item.id || "-")),
                              h("span", null, "创建时间 " + formatDateTime(item.createdAt)),
                              item.scope === "context"
                                ? h("span", null, "语境签名 " + (item.contextSignature || "-"))
                                : h("span", null, "作用范围 全部信息流"),
                              item.reviewId ? h("span", null, "来源复审 " + item.reviewId) : null,
                              item.note ? h("span", null, "备注 " + item.note) : null
                            )
                          );
                        })
                      ),
                h(
                  "div",
                  { className: "mod-pager", style: { marginTop: 12 } },
                  h(Button, {
                    size: "small",
                    disabled: cursorStack.length === 0 || loading,
                    onClick: function () {
                      const stack = cursorStack.slice();
                      const previous = stack.pop();
                      void loadBlocklist(previous || null, stack, scopeFilter);
                    },
                  }, "上一页"),
                  h(Button, {
                    size: "small",
                    disabled: !nextCursor || loading,
                    onClick: function () {
                      void loadBlocklist(nextCursor, cursorStack.concat([cursor || ""]), scopeFilter);
                    },
                  }, "下一页")
                )
              )
            )
          );
        }

        function ModerationPanel(props) {
          const request = props.request;
          const status = props.status;
          const refreshStatus = props.refreshStatus;
          const beginAdminMutation = props.beginAdminMutation;
          const endAdminMutation = props.endAdminMutation;
          const adminMutationInFlight = props.adminMutationInFlight;

          const config = (status && status.config) || {};
          const health = (status && status.health) || {};
          const cache = (status && status.cache) || {};
          const reviewStats = (status && status.reviews) || {};
          const credentialAlert = resolveAiCredentialAlert(config);
          const credentialReady = config.credentialStatus !== "missing_master_key";

          const [aiForm] = Form.useForm();
          const [apiKeyDraft, setApiKeyDraft] = React.useState("");
          const [aiDirty, setAiDirty] = React.useState(false);
          const [saveLoading, setSaveLoading] = React.useState(false);
          const [testLoading, setTestLoading] = React.useState(false);
          const [clearKeyLoading, setClearKeyLoading] = React.useState(false);
          const [testResult, setTestResult] = React.useState(null);
          const aiDirtyRef = React.useRef(false);

          const [reviewFilter, setReviewFilter] = React.useState("pending");
          const [reviewItems, setReviewItems] = React.useState([]);
          const [reviewCursor, setReviewCursor] = React.useState(null);
          const [reviewNextCursor, setReviewNextCursor] = React.useState(null);
          const [reviewCursorStack, setReviewCursorStack] = React.useState([]);
          const [reviewsLoading, setReviewsLoading] = React.useState(false);
          const [reviewDetail, setReviewDetail] = React.useState(null);
          const [approveTarget, setApproveTarget] = React.useState(null);
          const [approveScope, setApproveScope] = React.useState("context");
          const [approveNote, setApproveNote] = React.useState("");
          const [approveLoading, setApproveLoading] = React.useState(false);
          const [rejectTarget, setRejectTarget] = React.useState(null);
          const [rejectScope, setRejectScope] = React.useState("context");
          const [rejectNote, setRejectNote] = React.useState("");
          const [rejectLoading, setRejectLoading] = React.useState(false);

          const [allowScopeFilter, setAllowScopeFilter] = React.useState("");
          const [allowItems, setAllowItems] = React.useState([]);
          const [allowCursor, setAllowCursor] = React.useState(null);
          const [allowNextCursor, setAllowNextCursor] = React.useState(null);
          const [allowCursorStack, setAllowCursorStack] = React.useState([]);
          const [allowLoading, setAllowLoading] = React.useState(false);
          const [revokeLoadingId, setRevokeLoadingId] = React.useState(null);

          const [blockScopeFilter, setBlockScopeFilter] = React.useState("");
          const [blockItems, setBlockItems] = React.useState([]);
          const [blockCursor, setBlockCursor] = React.useState(null);
          const [blockNextCursor, setBlockNextCursor] = React.useState(null);
          const [blockCursorStack, setBlockCursorStack] = React.useState([]);
          const [blockLoading, setBlockLoading] = React.useState(false);
          const [revokeBlockLoadingId, setRevokeBlockLoadingId] = React.useState(null);

          React.useEffect(function () {
            if (!status || aiDirtyRef.current) return;
            aiForm.setFieldsValue({
              enabled: config.enabled === true,
              protocol: config.protocol || "openai_responses",
              endpoint: config.endpoint || "",
              model: config.model || "",
              allowPrivateNetwork: config.allowPrivateNetwork === true,
              jsonFallbackEnabled: config.jsonFallbackEnabled === true,
            });
          }, [status, aiForm]);

          const loadReviews = React.useCallback(async function (cursor, stack, filter) {
            setReviewsLoading(true);
            try {
              const params = new URLSearchParams();
              const activeFilter = filter === undefined ? reviewFilter : filter;
              if (activeFilter) params.set("status", activeFilter);
              params.set("limit", "50");
              if (cursor) params.set("cursor", cursor);
              const data = await request("/server-admin/moderation/reviews?" + params.toString());
              setReviewItems(data && Array.isArray(data.items) ? data.items : []);
              setReviewCursor(cursor || null);
              setReviewNextCursor(data && data.nextCursor ? data.nextCursor : null);
              setReviewCursorStack(stack || []);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "加载复审候选失败");
              }
            } finally {
              setReviewsLoading(false);
            }
          }, [request, reviewFilter]);

          const loadAllowlist = React.useCallback(async function (cursor, stack, scope) {
            setAllowLoading(true);
            try {
              const params = new URLSearchParams();
              const activeScope = scope === undefined ? allowScopeFilter : scope;
              if (activeScope) params.set("scope", activeScope);
              params.set("limit", "50");
              if (cursor) params.set("cursor", cursor);
              const data = await request("/server-admin/moderation/allowlist?" + params.toString());
              setAllowItems(data && Array.isArray(data.items) ? data.items : []);
              setAllowCursor(cursor || null);
              setAllowNextCursor(data && data.nextCursor ? data.nextCursor : null);
              setAllowCursorStack(stack || []);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "加载永久白名单失败");
              }
            } finally {
              setAllowLoading(false);
            }
          }, [request, allowScopeFilter]);

          const loadBlocklist = React.useCallback(async function (cursor, stack, scope) {
            setBlockLoading(true);
            try {
              const params = new URLSearchParams();
              const activeScope = scope === undefined ? blockScopeFilter : scope;
              if (activeScope) params.set("scope", activeScope);
              params.set("limit", "50");
              if (cursor) params.set("cursor", cursor);
              const data = await request("/server-admin/moderation/blocklist?" + params.toString());
              setBlockItems(data && Array.isArray(data.items) ? data.items : []);
              setBlockCursor(cursor || null);
              setBlockNextCursor(data && data.nextCursor ? data.nextCursor : null);
              setBlockCursorStack(stack || []);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "加载永久黑名单失败");
              }
            } finally {
              setBlockLoading(false);
            }
          }, [request, blockScopeFilter]);

          React.useEffect(function () {
            void loadReviews(null, [], reviewFilter);
          }, [reviewFilter]);

          React.useEffect(function () {
            void loadAllowlist(null, [], allowScopeFilter);
          }, [allowScopeFilter]);

          React.useEffect(function () {
            void loadBlocklist(null, [], blockScopeFilter);
          }, [blockScopeFilter]);

          function markAiDirty() {
            if (aiDirtyRef.current) return;
            aiDirtyRef.current = true;
            setAiDirty(true);
          }

          function clearAiDirty() {
            aiDirtyRef.current = false;
            setAiDirty(false);
          }

          async function handleAiSave(values) {
            if (!beginAdminMutation()) return;
            setSaveLoading(true);
            try {
              const patch = buildAiConfigPatch(values || aiForm.getFieldsValue(), apiKeyDraft);
              await request("/server-admin/moderation/ai", {
                method: "PATCH",
                headers: { "content-type": "application/json" },
                body: JSON.stringify(patch),
              });
              message.success("AI 审核配置已保存");
              setApiKeyDraft("");
              setTestResult(null);
              clearAiDirty();
              await refreshStatus({ source: "save" });
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "保存 AI 审核配置失败");
              }
            } finally {
              endAdminMutation();
              setSaveLoading(false);
            }
          }

          async function handleAiTest() {
            if (!beginAdminMutation()) return;
            setTestLoading(true);
            setTestResult(null);
            try {
              const body = buildAiTestRequest(aiForm.getFieldsValue(), apiKeyDraft);
              const result = await request("/server-admin/moderation/ai/test", {
                method: "POST",
                headers: { "content-type": "application/json" },
                body: JSON.stringify(body),
              });
              setTestResult({
                ok: true,
                structuredMode: result && result.structuredMode,
                latencyMs: result && result.latencyMs,
                decision: result && result.decision,
              });
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                setTestResult({
                  ok: false,
                  message: (error && error.message) || "测试连接失败",
                });
              }
            } finally {
              endAdminMutation();
              setTestLoading(false);
            }
          }

          function confirmClearApiKey() {
            Modal.confirm({
              title: "清除已保存的 API Key？",
              content: "清除后会自动停用 AI 审核；重新填写并保存新的 Key 后才能再次启用。其他配置保持不变。",
              okText: "确认清除",
              okButtonProps: { danger: true },
              cancelText: "取消",
              onOk: executeClearApiKey,
            });
          }

          async function executeClearApiKey() {
            if (!beginAdminMutation()) return;
            setClearKeyLoading(true);
            try {
              await request("/server-admin/moderation/ai", {
                method: "PATCH",
                headers: { "content-type": "application/json" },
                body: JSON.stringify({ enabled: false, apiKeyAction: "clear" }),
              });
              message.success("API Key 已清除");
              setApiKeyDraft("");
              setTestResult(null);
              await refreshStatus({ source: "save" });
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "清除 API Key 失败");
              }
            } finally {
              endAdminMutation();
              setClearKeyLoading(false);
            }
          }

          async function openReviewDetail(id) {
            setReviewDetail({ loading: true, data: null, error: null });
            try {
              const data = await request("/server-admin/moderation/reviews/" + encodeURIComponent(id));
              setReviewDetail({ loading: false, data: data, error: null });
            } catch (error) {
              if (error && (error.status === 401 || error.status === 403)) {
                setReviewDetail(null);
                return;
              }
              setReviewDetail({
                loading: false,
                data: null,
                error: (error && error.message) || "加载候选详情失败",
              });
            }
          }

          function openApproveModal(item) {
            setApproveScope("context");
            setApproveNote("");
            setApproveTarget(item);
          }

          async function executeApprove() {
            if (!approveTarget || !beginAdminMutation()) return;
            setApproveLoading(true);
            try {
              const note = approveNote.trim();
              await request(
                "/server-admin/moderation/reviews/" + encodeURIComponent(approveTarget.id) + "/approve",
                {
                  method: "POST",
                  headers: { "content-type": "application/json" },
                  body: JSON.stringify({
                    scope: approveScope,
                    ...(note ? { note: note } : {}),
                  }),
                });
              message.success(approveScope === "term" ? "已按整个词批准并永久放行" : "已按当前语境批准");
              setApproveTarget(null);
              setApproveNote("");
              setReviewDetail(null);
              await Promise.all([
                loadReviews(null, [], reviewFilter),
                loadAllowlist(null, [], allowScopeFilter),
                refreshStatus({ source: "save" }),
              ]);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "批准候选失败");
              }
            } finally {
              endAdminMutation();
              setApproveLoading(false);
            }
          }

          function openRejectModal(item) {
            setRejectScope("context");
            setRejectNote("");
            setRejectTarget(item);
          }

          async function executeReject() {
            if (!rejectTarget || !beginAdminMutation()) return;
            setRejectLoading(true);
            try {
              const note = rejectNote.trim();
              await request(
                "/server-admin/moderation/reviews/" + encodeURIComponent(rejectTarget.id) + "/reject",
                {
                  method: "POST",
                  headers: { "content-type": "application/json" },
                  body: JSON.stringify({
                    scope: rejectScope,
                    ...(note ? { note: note } : {}),
                  }),
                });
              message.success(rejectScope === "term" ? "已拒绝并将整个词加入永久黑名单" : "已拒绝并将当前语境加入永久黑名单");
              setRejectTarget(null);
              setRejectNote("");
              setReviewDetail(null);
              await Promise.all([
                loadReviews(null, [], reviewFilter),
                loadBlocklist(null, [], blockScopeFilter),
                refreshStatus({ source: "save" }),
              ]);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "拒绝候选失败");
              }
            } finally {
              endAdminMutation();
              setRejectLoading(false);
            }
          }

          function confirmRevokeAllowRule(item) {
            Modal.confirm({
              title: "撤销这条白名单规则？",
              content: "撤销后立即恢复对该词" + (item && item.scope === "term" ? "（含名称检查）" : "（该语境）") + "的审核/拒绝策略。",
              okText: "确认撤销",
              okButtonProps: { danger: true },
              cancelText: "取消",
              onOk: function () { return executeRevokeAllowRule(item); },
            });
          }

          async function executeRevokeAllowRule(item) {
            if (!item || !beginAdminMutation()) return;
            setRevokeLoadingId(item.id);
            try {
              await request("/server-admin/moderation/allowlist/" + encodeURIComponent(item.id), {
                method: "DELETE",
              });
              message.success("白名单规则已撤销");
              await Promise.all([
                loadAllowlist(null, [], allowScopeFilter),
                loadReviews(null, [], reviewFilter),
                refreshStatus({ source: "save" }),
              ]);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "撤销白名单规则失败");
              }
            } finally {
              endAdminMutation();
              setRevokeLoadingId(null);
            }
          }

          function confirmRevokeBlockRule(item) {
            Modal.confirm({
              title: "撤销这条黑名单规则？",
              content: "撤销后，相同内容将重新按永久白名单、安全缓存或 AI 审核策略处理。",
              okText: "确认撤销",
              okButtonProps: { danger: true },
              cancelText: "取消",
              onOk: function () { return executeRevokeBlockRule(item); },
            });
          }

          async function executeRevokeBlockRule(item) {
            if (!item || !beginAdminMutation()) return;
            setRevokeBlockLoadingId(item.id);
            try {
              await request("/server-admin/moderation/blocklist/" + encodeURIComponent(item.id), {
                method: "DELETE",
              });
              message.success("黑名单规则已撤销");
              await Promise.all([
                loadBlocklist(null, [], blockScopeFilter),
                loadReviews(null, [], reviewFilter),
                refreshStatus({ source: "save" }),
              ]);
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "撤销黑名单规则失败");
              }
            } finally {
              endAdminMutation();
              setRevokeBlockLoadingId(null);
            }
          }

          const recentErrors = Array.isArray(health.recentErrors) ? health.recentErrors : [];

          if (!status) {
            return h(
              "div",
              { className: "tab-panel tab-narrow" },
              h(
                Card,
                { className: "console-card", title: "AI 语义审核" },
                h(Spin, { tip: props.loading ? "正在加载 AI 审核状态..." : "等待 AI 审核状态..." })
              )
            );
          }

          return h(
            "div",
            { className: "tab-panel" },
            h(
              Space,
              { direction: "vertical", size: 16, style: { width: "100%" } },
              h(
                Card,
                { className: "console-card", title: "AI 语义审核" },
                h(
                  Space,
                  { direction: "vertical", size: 16, style: { width: "100%" } },
                  credentialAlert
                    ? h(Alert, {
                        type: credentialAlert.type,
                        showIcon: true,
                        message: credentialAlert.message,
                        description: credentialAlert.description,
                      })
                    : h(Alert, {
                        type: config.enabled === true ? "success" : "info",
                        showIcon: true,
                        message: config.enabled === true ? "AI 审核已启用" : "AI 审核未启用",
                        description: config.enabled === true
                          ? "命中敏感词的消息会先进入 AI 复核；未覆盖的命中按配置放行、打码或阻止。"
                          : "当前只使用确定性敏感词规则；配置完整后可在此启用语义复核。",
                      }),
                  aiDirty
                    ? h(Alert, {
                        type: "info",
                        showIcon: true,
                        message: "有未保存修改",
                        description: "自动刷新不会覆盖当前表单内容，保存或手动放弃后才会同步最新配置。",
                      })
                    : null,
                  h(
                    Form,
                    {
                      form: aiForm,
                      layout: "vertical",
                      disabled: adminMutationInFlight,
                      onFinish: handleAiSave,
                      onValuesChange: function () { markAiDirty(); },
                      initialValues: {
                        enabled: config.enabled === true,
                        protocol: config.protocol || "openai_responses",
                        endpoint: config.endpoint || "",
                        model: config.model || "",
                        allowPrivateNetwork: config.allowPrivateNetwork === true,
                        jsonFallbackEnabled: config.jsonFallbackEnabled === true,
                      },
                    },
                    h(
                      Form.Item,
                      {
                        name: "enabled",
                        label: "启用 AI 语义审核",
                        extra: credentialReady
                          ? "启用前必须已保存 API Key、完整请求地址和模型。仅公共服务器频道与房间富聊天 v2 会进入 AI 复核。"
                          : "缺少 AI_MODERATION_CREDENTIAL_KEY，无法启用。请先由运维配置主密钥。",
                      },
                      h(ToggleButton, {
                        activeLabel: "已启用",
                        inactiveLabel: "已停用",
                        disabled: !credentialReady,
                      })
                    ),
                    h(
                      Form.Item,
                      { name: "protocol", label: "协议" },
                      h(Select, { options: AI_PROTOCOL_OPTIONS })
                    ),
                    h(
                      Form.Item,
                      { name: "endpoint", label: "完整请求地址", extra: "服务端不会追加路径，请填写完整的请求 URL。默认只允许 HTTPS 公网地址。" },
                      h(Input, { placeholder: "https://api.openai.com/v1/responses", maxLength: 512 })
                    ),
                    h(
                      Form.Item,
                      { name: "model", label: "模型" },
                      h(Input, { placeholder: "模型 ID", maxLength: 160 })
                    ),
                    h(
                      Form.Item,
                      { label: "API Key", extra: config.apiKeyConfigured
                          ? "已保存 API Key。页面无法读取旧 Key；留空保存将保持现有 Key。"
                          : "Key 只会在保存时提交，任何读取接口都不会返回明文。" },
                      h(Input.Password, {
                        value: apiKeyDraft,
                        placeholder: config.apiKeyConfigured ? "留空保持不变，输入新 Key 则替换" : "请输入 API Key",
                        autoComplete: "new-password",
                        maxLength: 256,
                        onChange: function (event) {
                          setApiKeyDraft(event.target.value);
                          markAiDirty();
                        },
                      })
                    ),
                    h(
                      Form.Item,
                      {
                        name: "allowPrivateNetwork",
                        label: "允许访问私网模型",
                        extra: "仅在自托管内网端点时开启。开启后允许私网 IP 与 HTTP 地址。",
                      },
                      h(ToggleButton, null)
                    ),
                    h(
                      Form.Item,
                      {
                        name: "jsonFallbackEnabled",
                        label: "兼容提示词 JSON 回退",
                        extra: "模型不支持结构化输出时，退化为提示词 JSON 模式；关闭后此类提供商故障按打码降级处理。",
                      },
                      h(ToggleButton, null)
                    ),
                    h(
                      Form.Item,
                      { style: { marginBottom: 0 } },
                      h(
                        Space,
                        { size: 12, wrap: true, className: "settings-actions" },
                        h(Button, {
                          type: "primary",
                          htmlType: "submit",
                          loading: saveLoading,
                          disabled: adminMutationInFlight,
                        }, "保存配置"),
                        h(Button, {
                          onClick: function () { void handleAiTest(); },
                          loading: testLoading,
                          disabled: adminMutationInFlight,
                        }, "测试连接"),
                        h(Button, {
                          danger: true,
                          onClick: confirmClearApiKey,
                          loading: clearKeyLoading,
                          disabled: adminMutationInFlight || (!config.apiKeyConfigured && !apiKeyDraft),
                        }, "清除 API Key")
                      )
                    )
                  ),
                  testResult
                    ? testResult.ok
                      ? h(Alert, {
                          type: "success",
                          showIcon: true,
                          message: "测试连接成功",
                          description: "结构化模式 " + structuredModeLabel(testResult.structuredMode) + " · 延迟 " + (typeof testResult.latencyMs === "number" ? testResult.latencyMs + " ms" : "未记录") + "。测试不会保存配置或临时 Key。",
                        })
                      : h(Alert, {
                          type: "error",
                          showIcon: true,
                          message: "测试连接失败",
                          description: testResult.message,
                        })
                    : null
                )
              ),
              h(
                Card,
                {
                  className: "console-card",
                  title: h("div", { className: "section-title" }, h("span", null, "运行状态")),
                  extra: h(
                    Space,
                    { size: 8, wrap: true },
                    health.ready === true
                      ? h(Tag, { color: "success" }, "就绪")
                      : h(Tag, null, "未就绪"),
                    circuitStateTag(health)
                  ),
                },
                h(
                  Space,
                  { direction: "vertical", size: 16, style: { width: "100%" } },
                  h(
                    "div",
                    { className: "metric-grid" },
                    metricTile("活动请求", String(health.active || 0), "等待队列 " + (health.queued || 0), "info", ICONS.activity),
                    metricTile("平均延迟", (health.averageLatencyMs || 0) + " ms", "已开始 " + (health.started || 0) + " 次审核", "info", ICONS.gauge),
                    metricTile("放行 / 阻止", (health.allowed || 0) + " / " + (health.blocked || 0), "降级打码 " + (health.degraded || 0) + " 条", "good", ICONS.shield),
                    metricTile("待复审", String(reviewStats.pendingReviews || 0), "白名单 " + (reviewStats.allowRules || 0) + " · 黑名单 " + (reviewStats.blockRules || 0), (reviewStats.pendingReviews || 0) > 0 ? "warn" : "good", ICONS.target)
                  ),
                  h(
                    "div",
                    { className: "fact-chips" },
                    factChip("精确消息缓存命中", String(health.exactCacheHits || 0)),
                    factChip("语境缓存命中", String(health.contextCacheHits || 0)),
                    factChip("永久规则命中", String(health.permanentAllowHits || 0)),
                    factChip("永久黑名单命中", String(health.permanentBlockHits || 0)),
                    factChip("缓存条目（精确 / 语境）", (cache.exactEntries || 0) + " / " + (cache.contextEntries || 0)),
                    factChip("复审（批准 / 拒绝）", (reviewStats.approvedReviews || 0) + " / " + (reviewStats.rejectedReviews || 0))
                  ),
                  h(
                    "div",
                    null,
                    h("h3", { className: "detail-panel-title" }, svgIcon(ICONS.activity), h("span", null, "最近错误（不含聊天原文与密钥）")),
                    recentErrors.length === 0
                      ? h(Text, { type: "secondary" }, "暂无最近错误。")
                      : h(
                          "div",
                          { className: "mod-error-list" },
                          recentErrors.map(function (item, index) {
                            return h(
                              "div",
                              { key: index, className: "mod-error-row" },
                              h(
                                "span",
                                { className: "mod-error-code" },
                                item.code || "unknown",
                                typeof item.statusCode === "number" ? " · HTTP " + item.statusCode : ""
                              ),
                              h("span", { className: "mod-error-time" }, formatDateTime(item.at))
                            );
                          })
                        )
                  )
                )
              ),
              h(
                Card,
                {
                  className: "console-card",
                  title: h("div", { className: "section-title" }, h("span", null, "人工复审候选")),
                  extra: h(
                    Space,
                    { size: 8, wrap: true },
                    h(Select, {
                      value: reviewFilter,
                      style: { minWidth: 120 },
                      options: [
                        { value: "", label: "全部状态" },
                        { value: "pending", label: "待复审" },
                        { value: "approved", label: "已批准" },
                        { value: "rejected", label: "已拒绝" },
                      ],
                      onChange: function (value) { setReviewFilter(value); },
                    }),
                    h(Button, {
                      onClick: function () { void loadReviews(null, [], reviewFilter); },
                      loading: reviewsLoading,
                      disabled: adminMutationInFlight,
                    }, "刷新")
                  ),
                },
                reviewsLoading && reviewItems.length === 0
                  ? h(Spin, { tip: "正在加载复审候选..." })
                  : reviewItems.length === 0
                    ? h("div", { className: "announcement-empty" }, h(Text, { type: "secondary" }, "当前没有符合条件的复审候选。"))
                    : h(
                        "div",
                        { className: "mod-list" },
                        reviewItems.map(function (item) {
                          return h(
                            "div",
                            { key: item.id, className: "mod-item" },
                            h(
                              "div",
                              { className: "mod-item-head" },
                              h(
                                "div",
                                { className: "mod-item-title" },
                                h("span", null, item.displayTerm || item.normalizedTerm || "(未知词)"),
                                reviewStatusTag(item.status),
                                h(Tag, null, item.category || "未分类"),
                                h(Tag, { color: "purple" }, moderationSurfaceLabel(item.surface))
                              ),
                              h(
                                "div",
                                { className: "mod-item-actions" },
                                h(Button, {
                                  size: "small",
                                  onClick: function () { void openReviewDetail(item.id); },
                                }, "详情"),
                                item.status === "pending"
                                  ? h(Button, {
                                      size: "small",
                                      type: "primary",
                                      disabled: adminMutationInFlight,
                                      onClick: function () { openApproveModal(item); },
                                    }, "批准")
                                  : null,
                                item.status === "pending"
                                  ? h(Button, {
                                      size: "small",
                                      danger: true,
                                      disabled: adminMutationInFlight,
                                      onClick: function () { openRejectModal(item); },
                                    }, "拒绝")
                                  : null
                              )
                            ),
                            h(
                              "div",
                              { className: "mod-item-meta" },
                              h("span", null, "来源 " + (item.source || "未知")),
                              h("span", null, "观察 " + (item.observations || 0) + " 次"),
                              h("span", null, "AI 判定 " + (item.aiVerdict || "-") + " / " + (item.aiUsage || "-")),
                              h("span", null, "原因 " + (item.reasonCode || "-")),
                              h("span", null, "最近出现 " + formatDateTime(item.lastObservedAt))
                            )
                          );
                        })
                      ),
                h(
                  "div",
                  { className: "mod-pager", style: { marginTop: 12 } },
                  h(Button, {
                    size: "small",
                    disabled: reviewCursorStack.length === 0 || reviewsLoading,
                    onClick: function () {
                      const stack = reviewCursorStack.slice();
                      const previous = stack.pop();
                      void loadReviews(previous || null, stack, reviewFilter);
                    },
                  }, "上一页"),
                  h(Button, {
                    size: "small",
                    disabled: !reviewNextCursor || reviewsLoading,
                    onClick: function () {
                      void loadReviews(reviewNextCursor, reviewCursorStack.concat([reviewCursor || ""]), reviewFilter);
                    },
                  }, "下一页")
                )
              ),
              h(
                Card,
                {
                  className: "console-card",
                  title: h("div", { className: "section-title" }, h("span", null, "永久白名单")),
                  extra: h(
                    Space,
                    { size: 8, wrap: true },
                    h(Select, {
                      value: allowScopeFilter,
                      style: { minWidth: 120 },
                      options: [
                        { value: "", label: "全部范围" },
                        { value: "term", label: "整个词" },
                        { value: "context", label: "仅此语境" },
                      ],
                      onChange: function (value) { setAllowScopeFilter(value); },
                    }),
                    h(Button, {
                      onClick: function () { void loadAllowlist(null, [], allowScopeFilter); },
                      loading: allowLoading,
                      disabled: adminMutationInFlight,
                    }, "刷新")
                  ),
                },
                allowLoading && allowItems.length === 0
                  ? h(Spin, { tip: "正在加载永久白名单..." })
                  : allowItems.length === 0
                    ? h("div", { className: "announcement-empty" }, h(Text, { type: "secondary" }, "当前没有永久白名单规则。"))
                    : h(
                        "div",
                        { className: "mod-list" },
                        allowItems.map(function (item) {
                          return h(
                            "div",
                            { key: item.id, className: "mod-item" },
                            h(
                              "div",
                              { className: "mod-item-head" },
                              h(
                                "div",
                                { className: "mod-item-title" },
                                h("span", null, item.normalizedTerm || "(未知词)"),
                                allowScopeTag(item.scope),
                                h(Tag, { color: "purple" }, moderationSurfaceLabel(item.surface))
                              ),
                              h(
                                "div",
                                { className: "mod-item-actions" },
                                h(Button, {
                                  size: "small",
                                  danger: true,
                                  loading: revokeLoadingId === item.id,
                                  disabled: adminMutationInFlight,
                                  onClick: function () { confirmRevokeAllowRule(item); },
                                }, "撤销")
                              )
                            ),
                            h(
                              "div",
                              { className: "mod-item-meta" },
                              h("span", null, "创建时间 " + formatDateTime(item.createdAt)),
                              item.reviewId ? h("span", null, "来源复审 " + item.reviewId) : null,
                              item.note ? h("span", null, "备注 " + item.note) : null
                            )
                          );
                        })
                      ),
                h(
                  "div",
                  { className: "mod-pager", style: { marginTop: 12 } },
                  h(Button, {
                    size: "small",
                    disabled: allowCursorStack.length === 0 || allowLoading,
                    onClick: function () {
                      const stack = allowCursorStack.slice();
                      const previous = stack.pop();
                      void loadAllowlist(previous || null, stack, allowScopeFilter);
                    },
                  }, "上一页"),
                  h(Button, {
                    size: "small",
                    disabled: !allowNextCursor || allowLoading,
                    onClick: function () {
                      void loadAllowlist(allowNextCursor, allowCursorStack.concat([allowCursor || ""]), allowScopeFilter);
                    },
                  }, "下一页")
                )
              ),
              h(
                Card,
                {
                  className: "console-card",
                  title: h("div", { className: "section-title" }, h("span", null, "永久黑名单")),
                  extra: h(
                    Space,
                    { size: 8, wrap: true },
                    h(Select, {
                      value: blockScopeFilter,
                      style: { minWidth: 120 },
                      options: [
                        { value: "", label: "全部范围" },
                        { value: "term", label: "整个词" },
                        { value: "context", label: "仅此语境" },
                      ],
                      onChange: function (value) { setBlockScopeFilter(value); },
                    }),
                    h(Button, {
                      onClick: function () { void loadBlocklist(null, [], blockScopeFilter); },
                      loading: blockLoading,
                      disabled: adminMutationInFlight,
                    }, "刷新")
                  ),
                },
                blockLoading && blockItems.length === 0
                  ? h(Spin, { tip: "正在加载永久黑名单..." })
                  : blockItems.length === 0
                    ? h("div", { className: "announcement-empty" }, h(Text, { type: "secondary" }, "当前没有永久黑名单规则。"))
                    : h(
                        "div",
                        { className: "mod-list" },
                        blockItems.map(function (item) {
                          return h(
                            "div",
                            { key: item.id, className: "mod-item" },
                            h(
                              "div",
                              { className: "mod-item-head" },
                              h(
                                "div",
                                { className: "mod-item-title" },
                                h("span", null, item.normalizedTerm || "(未知词)"),
                                allowScopeTag(item.scope),
                                h(Tag, { color: "purple" }, moderationSurfaceLabel(item.surface))
                              ),
                              h(
                                "div",
                                { className: "mod-item-actions" },
                                h(Button, {
                                  size: "small",
                                  danger: true,
                                  loading: revokeBlockLoadingId === item.id,
                                  disabled: adminMutationInFlight,
                                  onClick: function () { confirmRevokeBlockRule(item); },
                                }, "撤销")
                              )
                            ),
                            h(
                              "div",
                              { className: "mod-item-meta" },
                              h("span", null, "创建时间 " + formatDateTime(item.createdAt)),
                              item.reviewId ? h("span", null, "来源复审 " + item.reviewId) : null,
                              item.note ? h("span", null, "备注 " + item.note) : null
                            )
                          );
                        })
                      ),
                h(
                  "div",
                  { className: "mod-pager", style: { marginTop: 12 } },
                  h(Button, {
                    size: "small",
                    disabled: blockCursorStack.length === 0 || blockLoading,
                    onClick: function () {
                      const stack = blockCursorStack.slice();
                      const previous = stack.pop();
                      void loadBlocklist(previous || null, stack, blockScopeFilter);
                    },
                  }, "上一页"),
                  h(Button, {
                    size: "small",
                    disabled: !blockNextCursor || blockLoading,
                    onClick: function () {
                      void loadBlocklist(blockNextCursor, blockCursorStack.concat([blockCursor || ""]), blockScopeFilter);
                    },
                  }, "下一页")
                )
              )
            ),
            h(
              Modal,
              {
                open: reviewDetail !== null,
                title: "复审候选详情",
                footer: null,
                onCancel: function () { setReviewDetail(null); },
              },
              !reviewDetail
                ? null
                : reviewDetail.loading
                  ? h(Spin, { tip: "正在加载详情..." })
                  : reviewDetail.error
                    ? h(Alert, { type: "error", showIcon: true, message: reviewDetail.error })
                    : reviewDetail.data
                      ? h(
                          "div",
                          { className: "mod-detail-grid" },
                          h(
                            "div",
                            { className: "mod-item-meta", style: { marginBottom: 8 } },
                            reviewStatusTag(reviewDetail.data.status),
                            h(Tag, null, reviewDetail.data.category || "未分类"),
                            h(Tag, null, structuredModeLabel(reviewDetail.data.structuredMode))
                          ),
                          detailRow("命中词", reviewDetail.data.displayTerm || "-"),
                          detailRow("规范化词", reviewDetail.data.normalizedTerm || "-"),
                          detailRow("信息类型", moderationSurfaceLabel(reviewDetail.data.surface)),
                          detailRow("来源", reviewDetail.data.source || "-"),
                          detailRow("语境签名", reviewDetail.data.contextSignature || "-"),
                          detailRow("观察次数", String(reviewDetail.data.observations || 0)),
                          detailRow("首次出现", formatDateTime(reviewDetail.data.firstObservedAt)),
                          detailRow("最近出现", formatDateTime(reviewDetail.data.lastObservedAt)),
                          detailRow("提供商 / 模型", (reviewDetail.data.provider || "-") + " / " + (reviewDetail.data.model || "-")),
                          detailRow("AI 结论", (reviewDetail.data.aiDecision || "-") + " / " + (reviewDetail.data.aiVerdict || "-") + " / " + (reviewDetail.data.aiUsage || "-")),
                          detailRow("原因码", reviewDetail.data.reasonCode || "-"),
                          detailRow("语境候选 ID", reviewDetail.data.aiContextCandidateId || "-"),
                          reviewDetail.data.note
                            ? detailRow("管理员备注", reviewDetail.data.note)
                            : null,
                          h("div", { className: "mod-evidence-label", style: { marginTop: 10 } }, "加密证据（仅本次详情请求解密）"),
                          reviewDetail.data.evidence
                            ? h(
                                Space,
                                { direction: "vertical", size: 8, style: { width: "100%" } },
                                h("div", { className: "mod-evidence-label" }, "原始消息"),
                                h("div", { className: "release-notes" }, reviewDetail.data.evidence.message || ""),
                                h("div", { className: "mod-evidence-label" }, "命中语境"),
                                h("div", { className: "release-notes" }, reviewDetail.data.evidence.context || "")
                              )
                            : h(Text, { type: "secondary" }, "证据已处理或已过期，无法再查看。"),
                          reviewDetail.data.status === "pending"
                            ? h(
                                Space,
                                { size: 8, style: { marginTop: 14 } },
                                h(Button, {
                                  type: "primary",
                                  disabled: adminMutationInFlight,
                                  onClick: function () { openApproveModal(reviewDetail.data); },
                                }, "批准"),
                                h(Button, {
                                  danger: true,
                                  disabled: adminMutationInFlight,
                                  onClick: function () { openRejectModal(reviewDetail.data); },
                                }, "拒绝")
                              )
                            : null
                        )
                      : null
            ),
            h(
              Modal,
              {
                open: approveTarget !== null,
                title: "批准复审候选",
                okText: "确认批准",
                cancelText: "取消",
                confirmLoading: approveLoading,
                okButtonProps: { disabled: adminMutationInFlight },
                onOk: function () { return executeApprove(); },
                onCancel: function () { if (!approveLoading) setApproveTarget(null); },
              },
              !approveTarget
                ? null
                : h(
                    Space,
                    { direction: "vertical", size: 12, style: { width: "100%" } },
                    h(Alert, {
                      type: approveScope === "term" ? "warning" : "info",
                      showIcon: true,
                      message: approveScope === "term" ? "整个词：全局永久放行" : "仅此语境：限定放行",
                      description: approveScope === "term"
                        ? "该规范化词将在聊天、房间名、用户名和角色名中全局永久放行。"
                        : "只放行相同信息类型中，相同规范化词与相同规范化语境的内容。",
                    }),
                    h(
                      Radio.Group,
                      {
                        value: approveScope,
                        onChange: function (event) { setApproveScope(event.target.value); },
                      },
                      h(Radio, { value: "context" }, "仅此语境（推荐）"),
                      h(Radio, { value: "term" }, "整个词")
                    ),
                    h(TextArea, {
                      rows: 3,
                      maxLength: 280,
                      placeholder: "管理员备注（可选）",
                      value: approveNote,
                      onChange: function (event) { setApproveNote(event.target.value); },
                    })
                  )
            ),
            h(
              Modal,
              {
                open: rejectTarget !== null,
                title: "拒绝复审候选",
                okText: "确认拒绝",
                cancelText: "取消",
                confirmLoading: rejectLoading,
                okButtonProps: { danger: true, disabled: adminMutationInFlight },
                onOk: function () { return executeReject(); },
                onCancel: function () { if (!rejectLoading) setRejectTarget(null); },
              },
              !rejectTarget
                ? null
                : h(
                    Space,
                    { direction: "vertical", size: 12, style: { width: "100%" } },
                    h(Alert, {
                      type: rejectScope === "term" ? "error" : "warning",
                      showIcon: true,
                      message: rejectScope === "term" ? "整个词：全局永久拦截" : "仅此语境：限定永久拦截",
                      description: rejectScope === "term"
                        ? "该规范化词将在聊天、房间名、用户名和角色名中永久拦截；撤销黑名单规则前不会再次调用 AI。"
                        : "只拦截相同信息类型中，相同规范化词与相同规范化语境的内容；撤销规则前不会再次调用 AI。",
                    }),
                    h(
                      Radio.Group,
                      {
                        value: rejectScope,
                        onChange: function (event) { setRejectScope(event.target.value); },
                      },
                      h(Radio, { value: "context" }, "仅此语境（推荐）"),
                      h(Radio, { value: "term" }, "整个词")
                    ),
                    h(TextArea, {
                      rows: 3,
                      maxLength: 280,
                      placeholder: "管理员备注（可选）",
                      value: rejectNote,
                      onChange: function (event) { setRejectNote(event.target.value); },
                    })
                  )
            )
          );
        }

        function App() {
          const [booting, setBooting] = React.useState(true);
          const [session, setSession] = React.useState(null);
          const [settings, setSettings] = React.useState(null);
          const [announcements, setAnnouncements] = React.useState([]);
          const [hasUnsavedDrafts, setHasUnsavedDrafts] = React.useState(false);
          const [pollRefreshDeferred, setPollRefreshDeferred] = React.useState(false);
          const [loginLoading, setLoginLoading] = React.useState(false);
          const [settingsLoading, setSettingsLoading] = React.useState(false);
          const [saveLoading, setSaveLoading] = React.useState(false);
          const [clearLoading, setClearLoading] = React.useState(false);
          const [logoutLoading, setLogoutLoading] = React.useState(false);
          const [updateCheckLoading, setUpdateCheckLoading] = React.useState(false);
          const [updateInstallLoading, setUpdateInstallLoading] = React.useState(false);
          const [adminMutationInFlight, setAdminMutationInFlight] = React.useState(false);
          const [activeTab, setActiveTab] = React.useState("overview");
          const [moderationStatus, setModerationStatus] = React.useState(null);
          const [moderationLoading, setModerationLoading] = React.useState(false);
          const [lastSyncedAt, setLastSyncedAt] = React.useState(0);
          const [loginForm] = Form.useForm();
          const [settingsForm] = Form.useForm();
          const settingsRef = React.useRef(null);
          const draftDirtyRef = React.useRef(false);
          const csrfTokenRef = React.useRef(null);
          const sessionGenerationRef = React.useRef(0);
          const settingsRequestSeqRef = React.useRef(0);
          const loginInFlightRef = React.useRef(false);
          const adminMutationInFlightRef = React.useRef(false);
          const clearConfirmOpenRef = React.useRef(false);
          const bandwidthHistoryRef = React.useRef([]);
          const watchedChatFeatures = Form.useWatch("chatFeatures", settingsForm);
          const chatControlState = resolveChatControlState(
            watchedChatFeatures || (settings && settings.chatFeatures) || null,
          );
          const statusAlert = settings ? buildStatusAlert(settings) : null;
          const updateStatus = (settings && settings.update) || {};
          const updatePhaseMeta = getUpdatePhaseMeta(updateStatus);

          function beginAdminMutation() {
            if (!beginSingleFlight(adminMutationInFlightRef)) return false;
            setAdminMutationInFlight(true);
            return true;
          }

          function endAdminMutation() {
            releaseSingleFlight(adminMutationInFlightRef);
            setAdminMutationInFlight(false);
          }

          const clearDraftDirty = React.useCallback(function () {
            draftDirtyRef.current = false;
            setHasUnsavedDrafts(false);
            setPollRefreshDeferred(false);
          }, []);

          const markDraftDirty = React.useCallback(function () {
            if (draftDirtyRef.current) {
              return;
            }

            draftDirtyRef.current = true;
            setHasUnsavedDrafts(true);
          }, []);

          const clearLocalAdminState = React.useCallback(function () {
            setSession(null);
            setSettings(null);
            setAnnouncements([]);
            setModerationStatus(null);
            settingsRef.current = null;
            loginForm.resetFields();
            settingsForm.resetFields();
            clearDraftDirty();
          }, [clearDraftDirty, loginForm, settingsForm]);

          const invalidateSession = React.useCallback(function () {
            sessionGenerationRef.current += 1;
            settingsRequestSeqRef.current += 1;
            csrfTokenRef.current = null;
            clearLocalAdminState();
          }, [clearLocalAdminState]);

          const request = React.useCallback(async function (path, options, expectedGeneration) {
            const requestGeneration = typeof expectedGeneration === "number"
              ? expectedGeneration
              : sessionGenerationRef.current;
            try {
              return await performAdminRequest(path, options, csrfTokenRef.current);
            } catch (error) {
              if (
                requestGeneration === sessionGenerationRef.current
                && error
                && error.status === 401
              ) {
                invalidateSession();
              } else if (
                requestGeneration === sessionGenerationRef.current
                && error
                && error.status === 403
              ) {
                message.error("安全令牌已失效，请重新登录后再操作");
              }
              throw error;
            }
          }, [invalidateSession]);

          const applySettingsSnapshot = React.useCallback(function (next, source) {
            const previous = settingsRef.current;
            const displaySnapshot = source === "poll"
              ? mergePollSnapshot(previous, next, draftDirtyRef.current)
              : next;
            if (typeof next.currentBandwidthMbps === "number" && isFinite(next.currentBandwidthMbps)) {
              const history = bandwidthHistoryRef.current;
              history.push(next.currentBandwidthMbps);
              if (history.length > 60) {
                history.splice(0, history.length - 60);
              }
            }
            settingsRef.current = displaySnapshot;
            setSettings(displaySnapshot);
            setLastSyncedAt(Date.now());
            if (source === "poll" && draftDirtyRef.current) {
              setPollRefreshDeferred(true);
            } else {
              setAnnouncements(normalizeAnnouncements(next.announcements));
              settingsForm.setFieldsValue({
                displayName: next.displayName || "",
                publicListingEnabled: Boolean(next.publicListingEnabled),
                modSyncEnabled: next.modSyncEnabled !== false,
                sensitiveFilterEnabled: next.sensitiveFilter ? next.sensitiveFilter.enabled !== false : true,
                bandwidthCapacityMbps: next.bandwidthCapacityMbps,
                chatFeatures: normalizeChatFeatures(next.chatFeatures),
              });
              clearDraftDirty();
            }
            notifySyncState(displaySnapshot, previous, source || "refresh");
          }, [clearDraftDirty, settingsForm]);

          const refreshSettings = React.useCallback(async function (options) {
            const source = options && options.source ? options.source : "refresh";
            if (source === "poll" && adminMutationInFlightRef.current) {
              return;
            }
            const showLoading = source !== "poll";
            const generation = sessionGenerationRef.current;
            const sequence = ++settingsRequestSeqRef.current;
            if (showLoading) {
              setSettingsLoading(true);
            }
            try {
              const next = await request("/server-admin/settings", undefined, generation);
              if (!isCurrentRequest(
                generation,
                sessionGenerationRef.current,
                sequence,
                settingsRequestSeqRef.current,
              )) {
                return;
              }
              applySettingsSnapshot(next, source);
            } catch (error) {
              if (error && (error.status === 401 || error.status === 403)) {
                return;
              }
              if (source === "poll") {
                notification.warning({
                  message: "状态刷新失败",
                  description: (error && error.message) ? error.message : "无法读取当前控制台状态。",
                  placement: "topRight",
                  duration: 5,
                });
              } else if (source === "manual") {
                message.error((error && error.message) ? error.message : "状态刷新失败");
              } else {
                throw error;
              }
            } finally {
              if (showLoading) {
                setSettingsLoading(false);
              }
            }
          }, [applySettingsSnapshot, request]);

          const refreshSession = React.useCallback(async function () {
            const generation = ++sessionGenerationRef.current;
            settingsRequestSeqRef.current += 1;
            setBooting(true);
            try {
              const nextSession = await request("/server-admin/session", undefined, generation);
              if (!isCurrentRequest(generation, sessionGenerationRef.current)) return;
              csrfTokenRef.current = nextSession.csrfToken;
              setSession(nextSession);
              await refreshSettings({ source: "initial" });
              void checkServiceUpdate(true);
            } catch (error) {
              if (!error || error.status !== 401) invalidateSession();
            } finally {
              setBooting(false);
            }
          }, [invalidateSession, refreshSettings, request]);

          React.useEffect(function () {
            void refreshSession();
          }, [refreshSession]);

          React.useEffect(function () {
            if (!session) {
              return;
            }

            const timer = setInterval(function () {
              void refreshSettings({ source: "poll" });
            }, 15000);

            return function () {
              clearInterval(timer);
            };
          }, [session, refreshSettings]);

          const refreshModeration = React.useCallback(async function (options) {
            const source = options && options.source ? options.source : "refresh";
            if (source === "poll" && adminMutationInFlightRef.current) {
              return;
            }
            const showLoading = source !== "poll";
            const generation = sessionGenerationRef.current;
            if (showLoading) {
              setModerationLoading(true);
            }
            try {
              const next = await request("/server-admin/moderation/ai", undefined, generation);
              if (!isCurrentRequest(generation, sessionGenerationRef.current)) {
                return;
              }
              setModerationStatus(next);
            } catch (error) {
              if (error && (error.status === 401 || error.status === 403)) {
                return;
              }
              if (source === "poll") {
                notification.warning({
                  message: "AI 审核状态刷新失败",
                  description: (error && error.message) ? error.message : "无法读取 AI 审核运行状态。",
                  placement: "topRight",
                  duration: 5,
                });
              } else {
                message.error((error && error.message) ? error.message : "AI 审核状态加载失败");
              }
            } finally {
              if (showLoading) {
                setModerationLoading(false);
              }
            }
          }, [request]);

          React.useEffect(function () {
            if (!session || (activeTab !== "moderation" && activeTab !== "blocklist")) {
              return;
            }

            void refreshModeration({ source: "initial" });
            const timer = setInterval(function () {
              void refreshModeration({ source: "poll" });
            }, 15000);

            return function () {
              clearInterval(timer);
            };
          }, [session, activeTab, refreshModeration]);

          async function handleLogin(values) {
            if (!beginSingleFlight(loginInFlightRef)) return;
            setLoginLoading(true);
            try {
              const nextSession = await performAdminRequest("/server-admin/login", {
                method: "POST",
                headers: { "content-type": "application/json" },
                body: JSON.stringify(values),
              }, null);
              sessionGenerationRef.current += 1;
              settingsRequestSeqRef.current += 1;
              csrfTokenRef.current = nextSession.csrfToken;
              setSession(nextSession);
              message.success("登录成功");
              await refreshSettings({ source: "initial" });
              void checkServiceUpdate(true);
            } catch (error) {
              if (csrfTokenRef.current) invalidateSession();
              message.error(error.message || "登录失败");
            } finally {
              loginInFlightRef.current = false;
              setLoginLoading(false);
            }
          }

          async function handleLogout() {
            if (!beginAdminMutation()) return;
            setLogoutLoading(true);
            const csrfToken = csrfTokenRef.current;
            sessionGenerationRef.current += 1;
            settingsRequestSeqRef.current += 1;
            try {
              await performAdminRequest(
                "/server-admin/logout",
                { method: "POST" },
                csrfToken,
              );
              message.success("已退出登录");
            } catch (error) {
              if (error && error.status === 403) {
                message.error("安全令牌已失效，本地登录状态已清除");
              } else if (!error || error.status !== 401) {
                message.error((error && error.message) || "退出登录失败，本地登录状态已清除");
              }
            } finally {
              csrfTokenRef.current = null;
              clearLocalAdminState();
              endAdminMutation();
              setLogoutLoading(false);
            }
          }

          function applyUpdateStatus(update) {
            if (!settingsRef.current) return;
            const next = { ...settingsRef.current, update: update };
            settingsRef.current = next;
            setSettings(next);
          }

          async function checkServiceUpdate(silent) {
            if (updateCheckLoading) return;
            setUpdateCheckLoading(true);
            try {
              const result = await request("/server-admin/update/check", { method: "POST" });
              applyUpdateStatus(result);
              if (!silent) {
                message.success(result.updateAvailable ? "发现可用服务端更新" : "当前已是最新版本");
              }
            } catch (error) {
              if (!silent && (!error || (error.status !== 401 && error.status !== 403))) {
                message.error((error && error.message) || "检查更新失败");
              }
            } finally {
              setUpdateCheckLoading(false);
            }
          }

          function watchServiceRestart() {
            let attempts = 0;
            const timer = setInterval(async function () {
              attempts += 1;
              try {
                const response = await fetch("/server-admin/session", { credentials: "same-origin", cache: "no-store" });
                if (response.status === 401) {
                  clearInterval(timer);
                  window.location.reload();
                  return;
                }
              } catch {
                // The service is between shutdown and restart.
              }
              if (attempts >= 180) clearInterval(timer);
            }, 2000);
          }

          async function executeServiceUpdate() {
            if (!beginAdminMutation()) return;
            setUpdateInstallLoading(true);
            try {
              const result = await request("/server-admin/update/install", { method: "POST" });
              applyUpdateStatus(result);
              message.success("更新任务已启动，完成后服务将自动重启");
              watchServiceRestart();
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error((error && error.message) || "启动更新失败");
              }
            } finally {
              endAdminMutation();
              setUpdateInstallLoading(false);
            }
          }

          function confirmServiceUpdate() {
            Modal.confirm({
              title: "安装服务端更新？",
              content: "系统会先完成下载、校验和构建，随后自动重启服务。现有房间连接会在重启时中断。",
              okText: "安装并重启",
              cancelText: "取消",
              onOk: executeServiceUpdate,
            });
          }

          async function handleSave(values) {
            if (!beginAdminMutation()) return;
            setSaveLoading(true);
            const generation = sessionGenerationRef.current;
            const sequence = ++settingsRequestSeqRef.current;
            try {
              const next = await request("/server-admin/settings", {
                method: "PATCH",
                headers: { "content-type": "application/json" },
                body: JSON.stringify({
                  ...values,
                  announcements: announcements,
                  chatFeatures: normalizeChatFeatures(values.chatFeatures),
                }),
              }, generation);
              if (!isCurrentRequest(
                generation,
                sessionGenerationRef.current,
                sequence,
                settingsRequestSeqRef.current,
              )) {
                return;
              }
              applySettingsSnapshot(next, "save");
              message.success("设置已保存");
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error(error.message || "保存失败");
              }
            } finally {
              endAdminMutation();
              setSaveLoading(false);
            }
          }

          async function saveAnnouncements() {
            await handleSave(settingsForm.getFieldsValue());
          }

          async function executeClearHistory() {
            if (!beginAdminMutation()) return;
            setClearLoading(true);
            const generation = sessionGenerationRef.current;
            settingsRequestSeqRef.current += 1;
            try {
              const result = await request(
                "/server-admin/chat/clear-history",
                { method: "POST" },
                generation,
              );
              if (!isCurrentRequest(generation, sessionGenerationRef.current)) return;
              const next = { ...settingsRef.current, metrics: result.metrics };
              settingsRef.current = next;
              setSettings(next);
              message.success("聊天历史已清空");
            } catch (error) {
              if (!error || (error.status !== 401 && error.status !== 403)) {
                message.error(error.message || "清空聊天历史失败");
              }
            } finally {
              endAdminMutation();
              setClearLoading(false);
            }
          }

          function confirmClearHistory() {
            if (clearConfirmOpenRef.current || adminMutationInFlightRef.current) return;
            clearConfirmOpenRef.current = true;
            Modal.confirm({
              title: "清空服务器聊天历史？",
              content: "现有服务器频道历史将立即清空，此操作无法撤销。",
              okText: "确认清空",
              okButtonProps: { danger: true },
              cancelText: "取消",
              onOk: async function () {
                try {
                  await executeClearHistory();
                } finally {
                  clearConfirmOpenRef.current = false;
                }
              },
              onCancel: function () {
                clearConfirmOpenRef.current = false;
              },
            });
          }

          async function handleManualRefresh() {
            if (adminMutationInFlightRef.current) return;
            if (draftDirtyRef.current && typeof window !== "undefined" && typeof window.confirm === "function") {
              const confirmed = window.confirm("当前有未保存的修改。重新加载会覆盖左侧设置和公告草稿，是否继续？");
              if (!confirmed) {
                return;
              }
            }

            await refreshSettings({ source: "manual" });
          }

          function addAnnouncement() {
            if (adminMutationInFlightRef.current) return;
            markDraftDirty();
            setAnnouncements(function (current) {
              return current.concat([createAnnouncementDraft({
                type: "info",
                title: "新公告",
                body: "填写需要在大厅顶部展示的公告内容。",
                enabled: true,
              })]);
            });
          }

          function updateAnnouncement(index, patch) {
            if (adminMutationInFlightRef.current) return;
            markDraftDirty();
            setAnnouncements(function (current) {
              return current.map(function (item, itemIndex) {
                return itemIndex === index ? { ...item, ...patch } : item;
              });
            });
          }

          function moveAnnouncement(index, direction) {
            if (adminMutationInFlightRef.current) return;
            markDraftDirty();
            setAnnouncements(function (current) {
              const nextIndex = index + direction;
              if (nextIndex < 0 || nextIndex >= current.length) {
                return current;
              }

              const output = current.slice();
              const [item] = output.splice(index, 1);
              output.splice(nextIndex, 0, item);
              return output;
            });
          }

          function removeAnnouncement(index) {
            if (adminMutationInFlightRef.current) return;
            markDraftDirty();
            setAnnouncements(function (current) {
              return current.filter(function (_item, itemIndex) {
                return itemIndex !== index;
              });
            });
          }

          if (booting) {
            return h(
              ConfigProvider,
              { theme: ADMIN_THEME },
              h(
                "div",
                { className: "login-shell" },
                h(Spin, { size: "large", tip: "正在加载控制台..." })
              )
            );
          }

          if (!session) {
            return h(
              ConfigProvider,
              { theme: ADMIN_THEME },
              h(
                "div",
                { className: "login-shell" },
                h(
                  "div",
                  { className: "login-wrap" },
                  h(
                    Card,
                    { className: "login-card" },
                    h(
                      "div",
                      { className: "login-brand" },
                      h("div", { className: "brand-mark" }, svgIcon(ICONS.server)),
                      h(Title, { level: 2, style: { marginTop: 0, marginBottom: 0 } }, "服务器控制台"),
                      h("span", { className: "version-chip" }, serviceVersionLabel)
                    ),
                    h(Paragraph, { type: "secondary", style: { marginBottom: 24, textAlign: "center" } }, "登录后查看和修改这台服务器的公开列表设置与同步状态。"),
                    h(
                      Form,
                      { form: loginForm, layout: "vertical", onFinish: handleLogin },
                      h(Form.Item, { name: "username", label: "用户名", initialValue: "admin", rules: [{ required: true, message: "请输入用户名" }] }, h(Input, { placeholder: "请输入用户名" })),
                      h(Form.Item, { name: "password", label: "密码", rules: [{ required: true, message: "请输入密码" }] }, h(Input.Password, { placeholder: "请输入密码" })),
                      h(Form.Item, { style: { marginBottom: 0 } }, h(Button, { type: "primary", htmlType: "submit", loading: loginLoading, block: true, size: "large" }, "登录"))
                    )
                  )
                )
              )
            );
          }

          if (!settings) {
            return h(
              ConfigProvider,
              { theme: ADMIN_THEME },
              h(
                "div",
                { className: "login-shell" },
                h(Spin, { size: "large", tip: "正在加载服务器设置..." })
              )
            );
          }

          const moderationPendingReviews = moderationStatus &&
              moderationStatus.reviews &&
              typeof moderationStatus.reviews.pendingReviews === "number" &&
              moderationStatus.reviews.pendingReviews > 0
            ? moderationStatus.reviews.pendingReviews
            : null;
          const moderationBlockRules = moderationStatus &&
              moderationStatus.reviews &&
              typeof moderationStatus.reviews.blockRules === "number" &&
              moderationStatus.reviews.blockRules > 0
            ? moderationStatus.reviews.blockRules
            : null;

          const navTabs = [
            { key: "overview", label: "概览", icon: ICONS.dashboard, dot: null, count: null },
            { key: "settings", label: "公开设置", icon: ICONS.sliders, dot: hasUnsavedDrafts ? "#f2b04d" : null, count: null },
            { key: "announcements", label: "大厅公告", icon: ICONS.megaphone, dot: null, count: announcements.length },
            { key: "moderation", label: "AI 审核", icon: ICONS.shield, dot: moderationPendingReviews ? "#f2b04d" : null, count: moderationPendingReviews },
            { key: "blocklist", label: "永久黑名单", icon: ICONS.ban, dot: moderationBlockRules ? "#e65c66" : null, count: moderationBlockRules },
            { key: "update", label: "服务更新", icon: ICONS.download, dot: updateStatus.updateAvailable ? "#f2b04d" : null, count: null },
          ];

          return h(
            ConfigProvider,
            { theme: ADMIN_THEME },
            h(
              Layout,
              { className: "page-shell" },
              h(
                Layout.Header,
                { className: "page-header" },
                h(
                  "div",
                  { className: "page-header-inner" },
                  h(
                    "div",
                    { className: "page-brand" },
                    h("div", { className: "brand-mark" }, svgIcon(ICONS.server)),
                    h(
                      "div",
                      { className: "page-brand-text" },
                      h(Title, { level: 3, className: "page-brand-title" }, "服务器控制台"),
                      h("span", { className: "version-chip" }, serviceVersionLabel + " · " + deploymentLabel(updateStatus.deploymentMode)),
                      h(Text, { type: "secondary", className: "page-brand-subtitle" }, "管理公开列表开关、显示名称和节点网络运行状态")
                    )
                  ),
                  h(
                    "div",
                    { className: "page-actions" },
                    h(Badge, { status: "processing", text: "已登录" }),
                    h(Button, { onClick: handleLogout, loading: logoutLoading, disabled: adminMutationInFlight }, "退出")
                  )
                )
              ),
              h(
                Layout.Content,
                { className: "page-content" },
                h(
                  "nav",
                  { className: "console-nav", "aria-label": "控制台功能导航" },
                  navTabs.map(function (tab) {
                    return h(
                      "button",
                      {
                        key: tab.key,
                        type: "button",
                        className: "console-nav-btn" + (activeTab === tab.key ? " active" : ""),
                        "aria-pressed": activeTab === tab.key,
                        onClick: function () { setActiveTab(tab.key); },
                      },
                      svgIcon(tab.icon),
                      h("span", null, tab.label),
                      typeof tab.count === "number" ? h("span", { className: "nav-count" }, tab.count) : null,
                      tab.dot ? h("i", { className: "nav-dot", style: { background: tab.dot } }) : null
                    );
                  })
                ),
                activeTab === "update"
                ? h(
                  "div",
                  { className: "tab-panel" },
                  h(
                    Card,
                    {
                      className: "console-card update-panel" + (updateStatus.updateAvailable ? " available" : ""),
                      style: { marginBottom: 16 },
                    },
                  h(
                    "div",
                    { className: "update-layout" },
                    h(
                      Space,
                      { direction: "vertical", size: 8 },
                      h(
                        "div",
                        { className: "version-line" },
                        h(Text, { strong: true }, "服务端版本"),
                        h("span", { className: "version-number" }, "v" + (updateStatus.currentVersion || serviceVersionLabel.replace(/^.*v/, ""))),
                        h(Tag, { color: updatePhaseMeta[0] }, updatePhaseMeta[1]),
                        h(Tag, null, deploymentLabel(updateStatus.deploymentMode))
                      ),
                      updateStatus.updateAvailable
                        ? h(Text, null, "新版本 v" + updateStatus.latestVersion + " 已可用，预检通过后可直接安装。")
                        : h(Text, { type: "secondary" }, updateStatus.checkedAt
                            ? "最近检查：" + formatDateTime(updateStatus.checkedAt)
                            : "登录后会自动检查 GitHub Release。"),
                      updateStatus.error
                        ? h(Alert, { type: "error", showIcon: true, message: updateStatus.error })
                        : null,
                      Array.isArray(updateStatus.preflight) && updateStatus.preflight.length > 0
                        ? h(Alert, {
                            type: "warning",
                            showIcon: true,
                            message: "更新预检查未通过",
                            description: updateStatus.preflight.join("；"),
                          })
                        : null
                    ),
                    h(
                      Space,
                      { wrap: true },
                      updateStatus.releaseUrl
                        ? h(Button, { href: updateStatus.releaseUrl, target: "_blank", rel: "noreferrer" }, "查看 Release")
                        : null,
                      h(Button, {
                        onClick: function () { void checkServiceUpdate(false); },
                        loading: updateCheckLoading,
                        disabled: updateInstallLoading || adminMutationInFlight,
                      }, "检查更新"),
                      updateStatus.updateAvailable
                        ? h(Button, {
                            type: "primary",
                            onClick: confirmServiceUpdate,
                            loading: updateInstallLoading,
                            disabled: !updateStatus.canUpdate || adminMutationInFlight,
                          }, "一键更新")
                        : null
                    )
                  )
                  ),
                  updateStatus.releaseNotes
                    ? h(
                        "div",
                        { className: "detail-panel" },
                        h("h3", { className: "detail-panel-title" }, svgIcon(ICONS.download), h("span", null, "Release 说明")),
                        h("div", { className: "release-notes" }, updateStatus.releaseNotes)
                      )
                    : null
                )
                : null,
                activeTab === "settings"
                ? h(
                  "div",
                  { className: "tab-panel tab-narrow" },
                  h(
                    Space,
                    { direction: "vertical", size: 16, style: { width: "100%" } },
                      hasUnsavedDrafts
                        ? h(Alert, {
                            type: pollRefreshDeferred ? "warning" : "info",
                            showIcon: true,
                            message: pollRefreshDeferred ? "有未保存修改，自动刷新不会覆盖当前草稿" : "有未保存修改",
                            description: pollRefreshDeferred
                              ? "右侧状态仍会自动刷新；左侧设置和公告草稿会继续保留，直到你保存或手动重新加载配置。"
                              : "右侧状态仍会自动刷新；左侧设置和公告草稿在保存前不会被自动覆盖。",
                          })
                        : null,
                      h(
                        Card,
                        { className: "console-card", title: "公开设置" },
                        h(
                          Space,
                          { direction: "vertical", size: 20, style: { width: "100%" } },
                          h(Alert, {
                            type: statusAlert ? statusAlert.type : "info",
                            showIcon: true,
                            message: statusAlert ? statusAlert.message : "申请状态读取中",
                            description: statusAlert ? statusAlert.description : "正在读取当前公开申请状态。",
                          }),
                            h(
                              Form,
                              {
                              form: settingsForm,
                              layout: "vertical",
                              disabled: adminMutationInFlight,
                              onFinish: handleSave,
                              onValuesChange: function () { markDraftDirty(); },
                              initialValues: {
                                displayName: settings.displayName || "",
                                publicListingEnabled: Boolean(settings.publicListingEnabled),
                                modSyncEnabled: settings.modSyncEnabled !== false,
                                sensitiveFilterEnabled: settings.sensitiveFilter ? settings.sensitiveFilter.enabled !== false : true,
                                bandwidthCapacityMbps: settings.bandwidthCapacityMbps,
                                chatFeatures: normalizeChatFeatures(settings.chatFeatures),
                              },
                            },
                            h(Form.Item, { name: "displayName", label: "显示名称" }, h(Input, { placeholder: "留空则使用默认名称", maxLength: 64 })),
                            h(
                              Form.Item,
                              { name: "bandwidthCapacityMbps", label: "控制带宽上限 (Mbps)", extra: "留空时自动回退到公共服务器控制台记录的近 7 天探针峰值带宽。" },
                              h(InputNumber, {
                                min: 1,
                                max: 100000,
                                step: 0.1,
                                precision: 2,
                                placeholder: "例如 50",
                                style: { width: "100%" },
                              })
                            ),
                            h(
                              Form.Item,
                              {
                                name: "publicListingEnabled",
                                label: "加入公开节点列表",
                                extra: "开启后，本服务器会通过去中心化网络对所有玩家可见。关闭后玩家无法在客户端列表中看到，但已知直连地址的人仍可连接。",
                              },
                              h(ToggleButton, { activeLabel: "公开中", inactiveLabel: "私有" })
                            ),
                            h(
                              Form.Item,
                              {
                                name: "modSyncEnabled",
                                label: "加入前 MOD 兼容预检与 Workshop 自动同步",
                                extra: "默认开启。关闭后，客户端会回退旧版加入流程，不再提供 Workshop 自动订阅或选择性禁用提示。",
                              },
                              h(ToggleButton, null)
                            ),
                            h(
                              Form.Item,
                              {
                                name: "sensitiveFilterEnabled",
                                label: "敏感词过滤",
                                extra: sensitiveFilterExtra(settings.sensitiveFilter),
                              },
                              h(ToggleButton, { activeLabel: "已开启", inactiveLabel: "已关闭" })
                            ),
                            h(Title, { level: 5, style: { marginTop: 4, marginBottom: 8 } }, "聊天治理（持久化开关）"),
                            h(
                              "div",
                              { className: "feature-grid" },
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "serverChatEnabled"], label: "服务器频道" },
                                h(ToggleButton, null)
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "richContentEnabled"], label: "富内容" },
                                h(ToggleButton, null)
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "emojiEnabled"], label: "Emoji" },
                                h(ToggleButton, { disabled: chatControlState.emojiEnabled })
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "itemRefsEnabled"], label: "物品引用" },
                                h(ToggleButton, { disabled: chatControlState.itemRefsEnabled })
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "roomChatV2Enabled"], label: "房间富聊天 v2" },
                                h(ToggleButton, null)
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "roomCombatRefsEnabled"], label: "房间战斗引用" },
                                h(ToggleButton, { disabled: chatControlState.roomCombatRefsEnabled })
                              )
                            ),
                            h(
                              Form.Item,
                              { style: { marginBottom: 0 } },
                              h(
                                Space,
                                { size: 12, className: "settings-actions" },
                                h(Button, { type: "primary", htmlType: "submit", loading: saveLoading, disabled: adminMutationInFlight }, "保存设置"),
                                h(Button, { onClick: function () { void handleManualRefresh(); }, loading: settingsLoading, disabled: adminMutationInFlight }, "重新加载配置"),
                                h(Button, { danger: true, onClick: confirmClearHistory, loading: clearLoading, disabled: adminMutationInFlight }, "清空聊天历史")
                              )
                            )
                          )
                        )
                      )
                    )
                  )
                  : null,
                activeTab === "announcements"
                ? h(
                  "div",
                  { className: "tab-panel tab-narrow" },
                  h(
                    Card,
                    {
                      className: "console-card",
                      title: h("div", { className: "section-title" }, h("span", null, "大厅公告")),
                          extra: h(
                            Space,
                            { size: 8, className: "announcement-actions" },
                            h(Button, { onClick: function () { void saveAnnouncements(); }, loading: saveLoading, disabled: adminMutationInFlight }, "保存公告"),
                            h(Button, { type: "primary", onClick: addAnnouncement, disabled: adminMutationInFlight }, "新增公告")
                          ),
                        },
                        h(
                          Space,
                          { direction: "vertical", size: 16, style: { width: "100%" } },
                          h(
                            Alert,
                            {
                              type: announcements.length > 0 ? "info" : "warning",
                              showIcon: true,
                              message: announcements.length > 0 ? "大厅公告会按列表顺序轮播展示" : "当前没有配置公告",
                              description: announcements.length > 0
                                ? "只有启用中的公告会通过公开接口返回给客户端大厅。修改后请点击“保存公告”或“保存设置”。"
                                : "客户端在没有配置公告时会显示默认提示文案。",
                            }
                          ),
                          announcements.length === 0
                            ? h(
                                "div",
                                { className: "announcement-empty" },
                                h(Text, { type: "secondary" }, "点击“新增公告”开始配置顶部轮播内容。")
                              )
                            : h(
                                "div",
                                { className: "announcement-list" },
                                announcements.map(function (item, index) {
                                  return h(
                                    "div",
                                    { key: item.id, className: "announcement-card" },
                                    h(
                                      "div",
                                      { className: "announcement-card-header" },
                                      h(
                                        Space,
                                        { direction: "vertical", size: 2 },
                                        h(Text, { strong: true }, "公告 #" + (index + 1)),
                                        h(Text, { type: "secondary" }, item.enabled ? "已启用并对客户端可见" : "已禁用，不会返回给客户端")
                                      ),
                                      h(
                                        "div",
                                        { className: "announcement-toolbar" },
                                        h(Button, { onClick: function () { moveAnnouncement(index, -1); }, disabled: index === 0 }, "上移"),
                                        h(Button, { onClick: function () { moveAnnouncement(index, 1); }, disabled: index === announcements.length - 1 }, "下移"),
                                        h(Button, { danger: true, onClick: function () { removeAnnouncement(index); } }, "删除")
                                      )
                                    ),
                                    h(
                                      "div",
                                      { className: "announcement-grid two-columns" },
                                      h(Input, {
                                        placeholder: "公告标题",
                                        value: item.title,
                                        maxLength: 64,
                                        onChange: function (event) {
                                          updateAnnouncement(index, { title: event.target.value });
                                        },
                                      }),
                                      h(Input, {
                                        placeholder: "日期，例如 2026-03-22",
                                        value: item.dateLabel,
                                        maxLength: 32,
                                        onChange: function (event) {
                                          updateAnnouncement(index, { dateLabel: event.target.value });
                                        },
                                      })
                                    ),
                                    h(
                                      "div",
                                      { className: "announcement-grid two-columns", style: { marginTop: 12 } },
                                      h(Select, {
                                        value: item.type,
                                        options: ANNOUNCEMENT_TYPES,
                                        onChange: function (value) {
                                          updateAnnouncement(index, { type: value });
                                        },
                                      }),
                                      h(
                                        Space,
                                        { size: 12, align: "center" },
                                        h(Text, { type: "secondary" }, "启用"),
                                        h(Switch, {
                                          checked: item.enabled,
                                          checkedChildren: "显示",
                                          unCheckedChildren: "隐藏",
                                          onChange: function (checked) {
                                            updateAnnouncement(index, { enabled: checked });
                                          },
                                        })
                                      )
                                    ),
                                    h(TextArea, {
                                      style: { marginTop: 12 },
                                      placeholder: "公告正文",
                                      value: item.body,
                                      maxLength: 280,
                                      showCount: true,
                                      onChange: function (event) {
                                        updateAnnouncement(index, { body: event.target.value });
                                      },
                                    })
                                  );
                                })
                              )
                        )
                      )
                  )
                  : null,
                activeTab === "moderation"
                ? h(ModerationPanel, {
                    status: moderationStatus,
                    loading: moderationLoading,
                    refreshStatus: refreshModeration,
                    request: request,
                    beginAdminMutation: beginAdminMutation,
                    endAdminMutation: endAdminMutation,
                    adminMutationInFlight: adminMutationInFlight,
                  })
                : null,
                activeTab === "blocklist"
                ? h(PermanentBlocklistPanel, {
                    refreshStatus: refreshModeration,
                    request: request,
                    beginAdminMutation: beginAdminMutation,
                    endAdminMutation: endAdminMutation,
                    adminMutationInFlight: adminMutationInFlight,
                  })
                : null,
                activeTab === "overview"
                ? h(
                  "section",
                  { className: "dashboard-stack", "aria-label": "服务器运行仪表盘" },
                  updateStatus.updateAvailable
                    ? h(
                        "button",
                        {
                          type: "button",
                          className: "update-teaser",
                          onClick: function () { setActiveTab("update"); },
                        },
                        h("span", null, "发现新版本 v" + updateStatus.latestVersion + "，点击前往服务更新"),
                        h("span", { className: "update-teaser-arrow" }, "→")
                      )
                    : null,
                  h(
                    "div",
                    { className: "section-title" },
                    h(Title, { level: 4, style: { margin: 0 } }, "运行仪表盘"),
                    renderListingStateTag(settings)
                  ),
                      h(
                        "div",
                        { className: "metric-grid" },
                        h(
                          "div",
                          { className: "metric-tile tone-info metric-tile-chart" },
                          h("div", { className: "metric-label metric-label-icon" }, svgIcon(ICONS.activity), "当前带宽"),
                          h("div", { className: "metric-value" }, formatMbps(settings.currentBandwidthMbps)),
                          h(Sparkline, { points: bandwidthHistoryRef.current }),
                          h("div", { className: "metric-note" }, "实时 relay 吞吐 · 每 15 秒采样")
                        ),
                        h(
                          "div",
                          { className: "metric-tile tone-" + (settings.bandwidthUtilizationRatio > 0.8 ? "warn" : "good") },
                          h("div", { className: "metric-label metric-label-icon" }, svgIcon(ICONS.gauge), "容量利用率"),
                          h(
                            "div",
                            { className: "metric-main" },
                            h("div", { className: "metric-value" }, formatRatio(settings.bandwidthUtilizationRatio)),
                            usageRing(settings.bandwidthUtilizationRatio, 54)
                          ),
                          h("div", { className: "metric-note" }, "有效容量 " + formatMbps(settings.resolvedCapacityMbps))
                        ),
                        metricTile(
                          "活跃 relay 房间",
                          formatMetricCount(settings.relayActiveRooms),
                          "Host " + formatMetricCount(settings.relayActiveHosts) + " · Client " + formatMetricCount(settings.relayActiveClients),
                          "good",
                          ICONS.network
                        ),
                        metricTile(
                          "聊天连接",
                          formatMetricCount(
                            formatMetricCount(settings.metrics && settings.metrics.serverConnectionCount)
                              + formatMetricCount(settings.metrics && settings.metrics.roomConnectionCount)
                          ),
                          "服务器 + 房间频道",
                          "info",
                          ICONS.chat
                        )
                      ),
                      h(Alert, {
                        type: statusAlert ? statusAlert.type : "info",
                        showIcon: true,
                        message: statusAlert ? statusAlert.message : "节点状态读取中",
                        description: statusAlert ? statusAlert.description : "正在读取节点网络状态。",
                      }),
                      h(
                        "div",
                        { className: "detail-panels" },
                        h(
                          "div",
                          { className: "detail-panel" },
                          h("h3", { className: "detail-panel-title" }, svgIcon(ICONS.gauge), h("span", null, "带宽与容量")),
                          h(
                            "div",
                            { className: "panel-visual-row" },
                            usageRing(settings.bandwidthUtilizationRatio, 86),
                            h(
                              "div",
                              { className: "panel-visual-main" },
                              h("div", { className: "panel-visual-value" }, formatRatio(settings.bandwidthUtilizationRatio) + " 利用率"),
                              utilizationMeter(settings.bandwidthUtilizationRatio),
                              h("div", { className: "panel-visual-note" }, "当前带宽 " + formatMbps(settings.currentBandwidthMbps))
                            )
                          ),
                          h(
                            "div",
                            { className: "fact-chips" },
                            factChip("有效容量", formatMbps(settings.resolvedCapacityMbps)),
                            factChip("手动容量", formatMbps(settings.bandwidthCapacityMbps)),
                            factChip("探针峰值（7 天）", formatMbps(settings.probePeak7dCapacityMbps)),
                            factChip("容量来源", renderCapacitySource(settings.capacitySource || "unknown"))
                          ),
                          detailRow("建房保护", renderGuardTag(settings.createRoomGuardStatus || "unknown", Boolean(settings.createRoomGuardApplies))),
                          detailRow("30 秒累计流量", formatBytes(settings.relayTrafficBytesInWindow)),
                          detailRow("流量窗口", formatWindowSeconds(settings.relayTrafficWindowMs))
                        ),
                        h(
                          "div",
                          { className: "detail-panel" },
                          h("h3", { className: "detail-panel-title" }, svgIcon(ICONS.network), h("span", null, "连接与消息")),
                          relayDonut(settings.relayActiveHosts, settings.relayActiveClients),
                          chatQualityMeter("服务器频道", settings.metrics && settings.metrics.serverAcceptedMessages, settings.metrics && settings.metrics.serverRejectedMessages),
                          chatQualityMeter("房间频道", settings.metrics && settings.metrics.roomAcceptedMessages, settings.metrics && settings.metrics.roomRejectedMessages),
                          detailRow("服务器拒绝率", formatRejectionRate(settings.metrics && settings.metrics.serverAcceptedMessages, settings.metrics && settings.metrics.serverRejectedMessages)),
                          detailRow("房间拒绝率", formatRejectionRate(settings.metrics && settings.metrics.roomAcceptedMessages, settings.metrics && settings.metrics.roomRejectedMessages)),
                          detailRow("服务器频道有效版本", formatFeatureVersions(settings.serverFeatures)),
                          detailRow("房间聊天有效版本", formatFeatureVersions(settings.roomFeatures)),
                          detailRow("保留历史 / epoch", formatMetricCount(settings.metrics && settings.metrics.serverRetainedHistoryCount) + " / " + formatMetricCount(settings.metrics && settings.metrics.historyEpoch))
                        )
                      )
                  )
                  : null
                ),
                h(
                  "footer",
                  { className: "page-footer" },
                  h(
                    "div",
                    { className: "footer-group" },
                    h("span", { className: "version-chip" }, serviceVersionLabel),
                    h("span", null, deploymentLabel(updateStatus.deploymentMode) + " 部署模式")
                  ),
                  h(
                    "div",
                    { className: "footer-group" },
                    h("span", { className: "footer-dot" }),
                    h("span", null, "每 15 秒自动刷新"),
                    h("span", null, "最近同步 " + (lastSyncedAt ? new Date(lastSyncedAt).toLocaleTimeString() : "尚未同步"))
                  )
                )
              )
          );
        }

        const rootElement = document.getElementById("root");
        if (ReactDOM.createRoot) {
          ReactDOM.createRoot(rootElement).render(h(App));
        } else {
          ReactDOM.render(h(App), rootElement);
        }
      })();
    </script>
  </body>
</html>`;
}
