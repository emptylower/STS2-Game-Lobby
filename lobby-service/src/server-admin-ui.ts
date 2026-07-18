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
          radial-gradient(circle at top left, rgba(22,119,255,0.08), transparent 22%),
          linear-gradient(180deg, #f5f7fa 0%, #eef3f8 100%);
        color: #1f2329;
        font-family: "PingFang SC", "Microsoft YaHei", sans-serif;
      }
      .page-shell {
        min-height: 100vh;
      }
      .page-header {
        position: sticky;
        top: 0;
        z-index: 8;
        height: auto;
        min-height: 0;
        line-height: normal;
        padding: 0;
        background: rgba(255,255,255,0.86);
        backdrop-filter: blur(10px);
        border-bottom: 1px solid rgba(5, 5, 5, 0.06);
      }
      .page-header-inner {
        max-width: 1280px;
        margin: 0 auto;
        padding: 20px 24px;
        display: flex;
        flex-wrap: wrap;
        align-items: flex-start;
        justify-content: space-between;
        gap: 16px;
      }
      .page-brand {
        display: flex;
        flex-direction: column;
        gap: 4px;
        min-width: 0;
        flex: 1 1 auto;
      }
      .page-brand-title {
        margin: 0 !important;
        line-height: 1.15;
        overflow-wrap: anywhere;
      }
      .page-brand-subtitle {
        line-height: 1.45;
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
        max-width: 1280px;
        margin: 0 auto;
        padding: 24px;
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
        max-width: 420px;
      }
      .login-card,
      .console-card {
        border-radius: 18px;
        box-shadow: 0 16px 36px rgba(15, 23, 42, 0.08);
        min-width: 0;
      }
      .console-card .ant-card-head-wrapper,
      .console-card .ant-card-head-title,
      .console-card .ant-card-extra {
        min-width: 0;
      }
      .status-block .ant-descriptions-item-label {
        width: 164px;
        color: #667085;
      }
      .status-block .ant-descriptions-view table {
        table-layout: fixed;
        width: 100%;
      }
      .status-block .ant-descriptions-item-content {
        min-width: 0;
        overflow-wrap: anywhere;
        word-break: break-word;
      }
      .section-title {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 12px;
      }
      .announcement-list {
        display: grid;
        gap: 14px;
      }
      .announcement-card {
        border: 1px solid rgba(5, 5, 5, 0.08);
        border-radius: 16px;
        padding: 16px;
        background: rgba(248, 250, 252, 0.75);
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
        border-radius: 16px;
        background: rgba(248, 250, 252, 0.8);
        border: 1px dashed rgba(5, 5, 5, 0.12);
      }
      .announcement-card .ant-input,
      .announcement-card .ant-input-affix-wrapper,
      .announcement-card .ant-select-selector,
      .announcement-card .ant-input-number {
        border-radius: 12px !important;
      }
      .announcement-card .ant-input-textarea textarea {
        min-height: 112px;
      }
      .feature-grid {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        gap: 4px 16px;
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
      }
      @media (max-width: 640px) {
        .page-header-inner {
          flex-direction: column;
          align-items: stretch;
          padding-top: 16px;
          padding-bottom: 16px;
          gap: 12px;
        }
        .page-brand-title {
          font-size: 24px !important;
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
        .status-block .ant-descriptions-item-label {
          width: 120px;
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
        .page-actions {
          justify-content: space-between;
        }
        .page-actions .ant-badge-status-text {
          font-size: 12px;
        }
        .status-block .ant-descriptions-item-label {
          width: 108px;
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

        const ANNOUNCEMENT_TYPES = [
          { value: "update", label: "更新" },
          { value: "event", label: "活动" },
          { value: "warning", label: "警告" },
          { value: "info", label: "信息" },
        ];

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
          const [adminMutationInFlight, setAdminMutationInFlight] = React.useState(false);
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
          const watchedChatFeatures = Form.useWatch("chatFeatures", settingsForm);
          const chatControlState = resolveChatControlState(
            watchedChatFeatures || (settings && settings.chatFeatures) || null,
          );
          const statusAlert = settings ? buildStatusAlert(settings) : null;

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
            settingsRef.current = displaySnapshot;
            setSettings(displaySnapshot);
            if (source === "poll" && draftDirtyRef.current) {
              setPollRefreshDeferred(true);
            } else {
              setAnnouncements(normalizeAnnouncements(next.announcements));
              settingsForm.setFieldsValue({
                displayName: next.displayName || "",
                publicListingEnabled: Boolean(next.publicListingEnabled),
                modSyncEnabled: next.modSyncEnabled !== false,
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
              "div",
              { className: "login-shell" },
              h(Spin, { size: "large", tip: "正在加载控制台..." })
            );
          }

          if (!session) {
            return h(
              ConfigProvider,
              {
                theme: {
                  token: {
                    colorPrimary: "#1677ff",
                    borderRadius: 14,
                    fontFamily: "'PingFang SC','Microsoft YaHei',sans-serif",
                  },
                },
              },
              h(
                "div",
                { className: "login-shell" },
                h(
                  "div",
                  { className: "login-wrap" },
                  h(
                    Card,
                    { className: "login-card" },
                    h(Title, { level: 2, style: { marginTop: 0, marginBottom: 8 } }, "服务器控制台"),
                    h(Paragraph, { type: "secondary", style: { marginBottom: 8 } }, serviceVersionLabel),
                    h(Paragraph, { type: "secondary", style: { marginBottom: 24 } }, "登录后查看和修改这台服务器的公开列表设置与同步状态。"),
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
              "div",
              { className: "login-shell" },
              h(Spin, { size: "large", tip: "正在加载服务器设置..." })
            );
          }

          return h(
            ConfigProvider,
            {
              theme: {
                token: {
                  colorPrimary: "#1677ff",
                  borderRadius: 14,
                  fontFamily: "'PingFang SC','Microsoft YaHei',sans-serif",
                },
              },
            },
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
                    h(Title, { level: 3, className: "page-brand-title" }, "服务器控制台"),
                    h(Text, { type: "secondary", className: "page-brand-subtitle" }, serviceVersionLabel),
                    h(Text, { type: "secondary", className: "page-brand-subtitle" }, "管理公开列表开关、显示名称和节点网络运行状态")
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
                  Row,
                  { gutter: [16, 16] },
                  h(
                    Col,
                    { xs: 24, lg: 10 },
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
                                valuePropName: "checked",
                                extra: "开启后，本服务器会通过去中心化网络对所有玩家可见。关闭后玩家无法在客户端列表中看到，但已知直连地址的人仍可连接。",
                              },
                              h(Switch, { checkedChildren: "公开", unCheckedChildren: "私有" })
                            ),
                            h(
                              Form.Item,
                              {
                                name: "modSyncEnabled",
                                label: "加入前 MOD 兼容预检与 Workshop 自动同步",
                                valuePropName: "checked",
                                extra: "默认开启。关闭后，v0.5.1 客户端会回退旧版加入流程，不再提供 Workshop 自动订阅或选择性禁用提示。",
                              },
                              h(Switch, { checkedChildren: "开启", unCheckedChildren: "关闭" })
                            ),
                            h(Title, { level: 5, style: { marginTop: 4, marginBottom: 8 } }, "聊天治理（持久化开关）"),
                            h(
                              "div",
                              { className: "feature-grid" },
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "serverChatEnabled"], label: "服务器频道", valuePropName: "checked" },
                                h(Switch, { checkedChildren: "开启", unCheckedChildren: "关闭" })
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "richContentEnabled"], label: "富内容", valuePropName: "checked" },
                                h(Switch, { checkedChildren: "开启", unCheckedChildren: "关闭" })
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "emojiEnabled"], label: "Emoji", valuePropName: "checked" },
                                h(Switch, { disabled: chatControlState.emojiEnabled, checkedChildren: "开启", unCheckedChildren: "关闭" })
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "itemRefsEnabled"], label: "物品引用", valuePropName: "checked" },
                                h(Switch, { disabled: chatControlState.itemRefsEnabled, checkedChildren: "开启", unCheckedChildren: "关闭" })
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "roomChatV2Enabled"], label: "房间富聊天 v2", valuePropName: "checked" },
                                h(Switch, { checkedChildren: "开启", unCheckedChildren: "关闭" })
                              ),
                              h(
                                Form.Item,
                                { name: ["chatFeatures", "roomCombatRefsEnabled"], label: "房间战斗引用", valuePropName: "checked" },
                                h(Switch, { disabled: chatControlState.roomCombatRefsEnabled, checkedChildren: "开启", unCheckedChildren: "关闭" })
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
                      ),
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
                  ),
                  h(
                    Col,
                    { xs: 24, lg: 14 },
                    h(
                      Card,
                      { className: "console-card", title: "当前状态" },
                      h(
                        Descriptions,
                        { bordered: true, size: "middle", column: 1, className: "status-block" },
                        h(Descriptions.Item, { label: "服务器版本" }, serviceVersionLabel),
                        h(Descriptions.Item, { label: "公开列表" }, renderListingStateTag(settings)),
                        h(Descriptions.Item, { label: "说明" }, getListingStateMeta(settings).description),
                        h(Descriptions.Item, { label: "建房保护" }, renderGuardTag(settings.createRoomGuardStatus || "unknown", Boolean(settings.createRoomGuardApplies))),
                        h(Descriptions.Item, { label: "当前带宽" }, formatMbps(settings.currentBandwidthMbps)),
                        h(Descriptions.Item, { label: "30秒累计流量" }, formatBytes(settings.relayTrafficBytesInWindow)),
                        h(Descriptions.Item, { label: "流量窗口" }, formatWindowSeconds(settings.relayTrafficWindowMs)),
                        h(Descriptions.Item, { label: "relay 房间" }, typeof settings.relayActiveRooms === "number" ? settings.relayActiveRooms : "未记录"),
                        h(Descriptions.Item, { label: "relay Host" }, typeof settings.relayActiveHosts === "number" ? settings.relayActiveHosts : "未记录"),
                        h(Descriptions.Item, { label: "relay Client" }, typeof settings.relayActiveClients === "number" ? settings.relayActiveClients : "未记录"),
                        h(Descriptions.Item, { label: "手动容量" }, formatMbps(settings.bandwidthCapacityMbps)),
                        h(Descriptions.Item, { label: "探针峰值(7天)" }, formatMbps(settings.probePeak7dCapacityMbps)),
                        h(Descriptions.Item, { label: "有效容量" }, formatMbps(settings.resolvedCapacityMbps)),
                        h(Descriptions.Item, { label: "当前利用率" }, formatRatio(settings.bandwidthUtilizationRatio)),
                        h(Descriptions.Item, { label: "容量来源" }, renderCapacitySource(settings.capacitySource || "unknown"))
                        ,h(Descriptions.Item, { label: "服务器频道有效版本" }, formatFeatureVersions(settings.serverFeatures))
                        ,h(Descriptions.Item, { label: "房间聊天有效版本" }, formatFeatureVersions(settings.roomFeatures))
                        ,h(Descriptions.Item, { label: "服务器聊天连接" }, formatMetricCount(settings.metrics && settings.metrics.serverConnectionCount))
                        ,h(Descriptions.Item, { label: "房间聊天连接" }, formatMetricCount(settings.metrics && settings.metrics.roomConnectionCount))
                        ,h(Descriptions.Item, { label: "服务器保留历史" }, formatMetricCount(settings.metrics && settings.metrics.serverRetainedHistoryCount))
                        ,h(Descriptions.Item, { label: "历史 epoch" }, formatMetricCount(settings.metrics && settings.metrics.historyEpoch))
                        ,h(Descriptions.Item, { label: "服务器接受消息" }, formatMetricCount(settings.metrics && settings.metrics.serverAcceptedMessages))
                        ,h(Descriptions.Item, { label: "服务器拒绝消息" }, formatMetricCount(settings.metrics && settings.metrics.serverRejectedMessages))
                        ,h(Descriptions.Item, { label: "房间接受消息" }, formatMetricCount(settings.metrics && settings.metrics.roomAcceptedMessages))
                        ,h(Descriptions.Item, { label: "房间拒绝消息" }, formatMetricCount(settings.metrics && settings.metrics.roomRejectedMessages))
                        ,h(Descriptions.Item, { label: "服务器拒绝率" }, formatRejectionRate(
                          settings.metrics && settings.metrics.serverAcceptedMessages,
                          settings.metrics && settings.metrics.serverRejectedMessages,
                        ))
                        ,h(Descriptions.Item, { label: "房间拒绝率" }, formatRejectionRate(
                          settings.metrics && settings.metrics.roomAcceptedMessages,
                          settings.metrics && settings.metrics.roomRejectedMessages,
                        ))
                      )
                    )
                  )
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
