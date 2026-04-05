export function renderServerAdminPage() {
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
      }
      .status-block .ant-descriptions-item-label {
        width: 164px;
        color: #667085;
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
      @media (max-width: 992px) {
        .page-header-inner,
        .page-content {
          padding-left: 16px;
          padding-right: 16px;
        }
        .announcement-grid.two-columns {
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

        const ANNOUNCEMENT_TYPES = [
          { value: "update", label: "更新" },
          { value: "event", label: "活动" },
          { value: "warning", label: "警告" },
          { value: "info", label: "信息" },
        ];

        async function readJson(path, options) {
          const response = await fetch(path, options);
          const text = await response.text();
          const payload = text ? JSON.parse(text) : {};
          if (!response.ok) {
            throw new Error(payload.message || response.statusText || "请求失败");
          }
          return payload;
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

        function getSyncMeta(value) {
          const mapping = {
            heartbeat_ok: {
              tagColor: "success",
              tagLabel: "已在公开列表",
              toastType: "success",
              toastTitle: "公开列表同步正常",
              toastDescription: "这台子服务器已经在公开列表中持续同步。",
            },
            approved: {
              tagColor: "processing",
              tagLabel: "已获批",
              toastType: "success",
              toastTitle: "公开申请已获批",
              toastDescription: "母面板已签发服务器令牌，正在进入公开列表。",
            },
            submission_created: {
              tagColor: "processing",
              tagLabel: "申请已提交",
              toastType: "info",
              toastTitle: "公开申请已发出",
              toastDescription: "母面板已返回申请编号，等待进一步审核。",
            },
            pending_review: {
              tagColor: "warning",
              tagLabel: "待审核",
              toastType: "info",
              toastTitle: "公开申请待审核",
              toastDescription: "申请已经提交，母面板暂未返回审核结果。",
            },
            rejected: {
              tagColor: "error",
              tagLabel: "已拒绝",
              toastType: "warning",
              toastTitle: "公开申请被拒绝",
              toastDescription: "请根据审核备注调整配置后重新提交。",
            },
            submission_failed: {
              tagColor: "error",
              tagLabel: "申请发送失败",
              toastType: "error",
              toastTitle: "公开申请未发出",
              toastDescription: "子服务器向母面板发起申请时失败，请检查母面板地址和网络连通性。",
            },
            claim_failed: {
              tagColor: "warning",
              tagLabel: "审核状态获取失败",
              toastType: "warning",
              toastTitle: "申请已提交，但状态确认失败",
              toastDescription: "子服务器已经拿到申请编号，但暂时无法从母面板获取审核结果。",
            },
            heartbeat_failed: {
              tagColor: "warning",
              tagLabel: "公开同步失败",
              toastType: "warning",
              toastTitle: "公开列表同步失败",
              toastDescription: "子服务器可能已经获批，但当前公开列表心跳同步失败。",
            },
            public_endpoint_invalid: {
              tagColor: "error",
              tagLabel: "公网地址无效",
              toastType: "error",
              toastTitle: "公网地址配置错误",
              toastDescription: "当前上报给母面板的地址不是公网可达地址，母面板无法接收这台子服务器。",
            },
            listing_disable_failed: {
              tagColor: "warning",
              tagLabel: "取消公开失败",
              toastType: "warning",
              toastTitle: "取消公开同步失败",
              toastDescription: "子服务器通知母面板下线时失败，稍后会继续重试。",
            },
            sync_failed: {
              tagColor: "error",
              tagLabel: "同步失败",
              toastType: "error",
              toastTitle: "同步失败",
              toastDescription: "发生未分类的同步错误，请检查最近错误和服务端日志。",
            },
            listing_disabled: {
              tagColor: "default",
              tagLabel: "未公开",
            },
            registry_disabled: {
              tagColor: "default",
              tagLabel: "未配置母面板",
              toastType: "warning",
              toastTitle: "未配置母面板地址",
              toastDescription: "SERVER_REGISTRY_BASE_URL 未配置，公开申请不会发出。",
            },
            idle: {
              tagColor: "default",
              tagLabel: "未申请",
            },
          };
          return mapping[value] || {
            tagColor: "default",
            tagLabel: value || "未知",
          };
        }

        function renderSyncTag(value) {
          const meta = getSyncMeta(value);
          return h(Tag, { color: meta.tagColor }, meta.tagLabel);
        }

        function getListingStateMeta(settings) {
          if (!settings) {
            return {
              tagColor: "default",
              tagLabel: "未知",
              alertType: "info",
              description: "正在读取公开申请状态。",
            };
          }

          const syncStatus = settings.lastSyncStatus || "idle";
          if (!settings.publicListingEnabled) {
            if (syncStatus === "listing_disable_failed") {
              return {
                tagColor: "warning",
                tagLabel: "关闭公开失败",
                alertType: "warning",
                description: "你已经关闭了公开开关，但子服务器通知母面板下线时失败，稍后会继续重试。",
              };
            }

            if (settings.serverId || settings.submissionId || syncStatus === "listing_disabled") {
              return {
                tagColor: "default",
                tagLabel: "已关闭公开",
                alertType: "info",
                description: "公开申请开关已关闭，这台子服务器不会显示在公开列表中。",
              };
            }

            return {
              tagColor: "default",
              tagLabel: "未申请",
              alertType: "info",
              description: "当前尚未向母面板发起公开申请。",
            };
          }

          if (syncStatus === "submission_created") {
            return {
              tagColor: "processing",
              tagLabel: "已提交申请",
              alertType: "info",
              description: "申请已经成功发到母面板，等待母面板进一步审核。",
            };
          }

          if (syncStatus === "pending_review") {
            return {
              tagColor: "warning",
              tagLabel: "待审核",
              alertType: "warning",
              description: "申请已提交，但母面板暂未返回审核结果。",
            };
          }

          if (syncStatus === "rejected") {
            return {
              tagColor: "error",
              tagLabel: "已拒绝",
              alertType: "error",
              description: settings.lastReviewNote || "母面板拒绝了这次公开申请。",
            };
          }

          if (syncStatus === "heartbeat_ok") {
            return {
              tagColor: "success",
              tagLabel: "已加入公开列表",
              alertType: "success",
              description: "母面板已经接收并持续同步这台子服务器，它应当出现在公开列表中。",
            };
          }

          if (syncStatus === "approved") {
            return {
              tagColor: "processing",
              tagLabel: "已获批，等待同步",
              alertType: "info",
              description: "母面板已经签发服务器令牌，正在把这台子服务器加入公开列表。",
            };
          }

          if (syncStatus === "heartbeat_failed") {
            return {
              tagColor: "warning",
              tagLabel: settings.serverId || settings.hasServerToken ? "已获批，同步异常" : "公开同步失败",
              alertType: "warning",
              description: settings.lastSyncError || "子服务器和母面板之间的公开列表同步失败。",
            };
          }

          if (syncStatus === "submission_failed") {
            return {
              tagColor: "error",
              tagLabel: "申请未发出",
              alertType: "error",
              description: settings.lastSyncError || "子服务器向母面板发起申请时失败。",
            };
          }

          if (syncStatus === "claim_failed") {
            return {
              tagColor: "warning",
              tagLabel: "申请已提交，状态确认失败",
              alertType: "warning",
              description: settings.lastSyncError || "子服务器已经保存申请编号，但暂时无法从母面板确认审核结果。",
            };
          }

          if (syncStatus === "public_endpoint_invalid") {
            return {
              tagColor: "error",
              tagLabel: "公网地址配置错误",
              alertType: "error",
              description: settings.lastSyncError || "当前上报给母面板的地址不是公网可达地址。",
            };
          }

          if (syncStatus === "registry_disabled") {
            return {
              tagColor: "warning",
              tagLabel: "未配置母面板",
              alertType: "warning",
              description: "SERVER_REGISTRY_BASE_URL 未配置，公开申请不会发出。",
            };
          }

          if (syncStatus === "sync_failed") {
            return {
              tagColor: "error",
              tagLabel: "同步失败",
              alertType: "error",
              description: settings.lastSyncError || "发生未分类的同步错误，请检查日志。",
            };
          }

          if (settings.submissionId && !settings.serverId) {
            return {
              tagColor: "warning",
              tagLabel: "已申请，待审核",
              alertType: "warning",
              description: "子服务器已经拿到申请编号，等待母面板返回审核结果。",
            };
          }

          if (settings.serverId && settings.hasServerToken) {
            return {
              tagColor: "processing",
              tagLabel: "已获批",
              alertType: "info",
              description: "母面板已经签发服务器令牌，等待首轮心跳同步。",
            };
          }

          return {
            tagColor: "processing",
            tagLabel: "准备申请",
            alertType: "info",
            description: "公开申请已经启用，子服务器正在进行首轮同步。",
          };
        }

        function renderListingStateTag(settings) {
          const meta = getListingStateMeta(settings);
          return h(Tag, { color: meta.tagColor }, meta.tagLabel);
        }

        function buildStatusAlert(settings) {
          const meta = getListingStateMeta(settings);
          return {
            type: meta.alertType,
            message: "申请状态：" + meta.tagLabel,
            description: h(
              Space,
              { direction: "vertical", size: 2 },
              h("span", null, meta.description),
              settings && settings.lastSyncError
                ? h(Text, { type: "secondary" }, "最近错误：" + settings.lastSyncError)
                : null,
              settings && settings.lastReviewNote
                ? h(Text, { type: "secondary" }, "审核备注：" + settings.lastReviewNote)
                : null,
            ),
          };
        }

        function buildSyncSignature(settings) {
          if (!settings) {
            return "";
          }

          return [
            settings.publicListingEnabled ? "1" : "0",
            settings.lastSyncStatus || "",
            settings.lastSyncError || "",
            settings.lastReviewNote || "",
            settings.submissionId || "",
            settings.serverId || "",
            settings.hasServerToken ? "1" : "0",
          ].join("|");
        }

        function notifySyncState(next, previous, source) {
          const meta = getSyncMeta(next.lastSyncStatus || "idle");
          if (!meta.toastType || !meta.toastTitle) {
            return;
          }

          const previousSignature = buildSyncSignature(previous);
          const nextSignature = buildSyncSignature(next);
          if (previousSignature === nextSignature) {
            return;
          }

          if (!previous && meta.toastType !== "error" && meta.toastType !== "warning") {
            return;
          }

          if (source === "save" && meta.toastType === "info") {
            return;
          }

          let description = meta.toastDescription || "";
          if (next.lastSyncError && (meta.toastType === "error" || meta.toastType === "warning")) {
            description = description
              ? description + " 最近错误：" + next.lastSyncError
              : next.lastSyncError;
          }

          if (next.lastReviewNote && next.lastSyncStatus === "rejected") {
            description = description
              ? description + " 审核备注：" + next.lastReviewNote
              : next.lastReviewNote;
          }

          notification[meta.toastType]({
            message: meta.toastTitle,
            description: description || undefined,
            placement: "topRight",
            duration: meta.toastType === "error" ? 8 : 6,
          });
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
          const [loginForm] = Form.useForm();
          const [settingsForm] = Form.useForm();
          const settingsRef = React.useRef(null);
          const draftDirtyRef = React.useRef(false);
          const statusAlert = settings ? buildStatusAlert(settings) : null;

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

          const applySettingsSnapshot = React.useCallback(function (next, source) {
            const previous = settingsRef.current;
            settingsRef.current = next;
            setSettings(next);
            if (source === "poll" && draftDirtyRef.current) {
              setPollRefreshDeferred(true);
            } else {
              setAnnouncements(normalizeAnnouncements(next.announcements));
              settingsForm.setFieldsValue({
                displayName: next.displayName || "",
                publicListingEnabled: Boolean(next.publicListingEnabled),
                bandwidthCapacityMbps: next.bandwidthCapacityMbps,
              });
              clearDraftDirty();
            }
            notifySyncState(next, previous, source || "refresh");
          }, [clearDraftDirty, settingsForm]);

          const refreshSettings = React.useCallback(async function (options) {
            const source = options && options.source ? options.source : "refresh";
            const showLoading = source !== "poll";
            if (showLoading) {
              setSettingsLoading(true);
            }
            try {
              const next = await readJson("/server-admin/settings");
              applySettingsSnapshot(next, source);
            } catch (error) {
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
          }, [applySettingsSnapshot]);

          const refreshSession = React.useCallback(async function () {
            setBooting(true);
            try {
              const nextSession = await readJson("/server-admin/session");
              setSession(nextSession);
              await refreshSettings({ source: "initial" });
            } catch (_error) {
              setSession(null);
              setSettings(null);
              setAnnouncements([]);
              settingsRef.current = null;
              clearDraftDirty();
            } finally {
              setBooting(false);
            }
          }, [clearDraftDirty, refreshSettings]);

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
            setLoginLoading(true);
            try {
              await readJson("/server-admin/login", {
                method: "POST",
                headers: { "content-type": "application/json" },
                body: JSON.stringify(values),
              });
              message.success("登录成功");
              await refreshSession();
            } catch (error) {
              message.error(error.message || "登录失败");
            } finally {
              setLoginLoading(false);
            }
          }

          async function handleLogout() {
            await fetch("/server-admin/logout", { method: "POST" });
            loginForm.resetFields();
            settingsForm.resetFields();
            setSession(null);
            setSettings(null);
            setAnnouncements([]);
            settingsRef.current = null;
            clearDraftDirty();
            message.success("已退出登录");
          }

          async function handleSave(values) {
            setSaveLoading(true);
            try {
              const next = await readJson("/server-admin/settings", {
                method: "PATCH",
                headers: { "content-type": "application/json" },
                body: JSON.stringify({
                  ...values,
                  announcements: announcements,
                }),
              });
              applySettingsSnapshot(next, "save");
              message.success("设置已保存");
            } catch (error) {
              message.error(error.message || "保存失败");
            } finally {
              setSaveLoading(false);
            }
          }

          async function saveAnnouncements() {
            await handleSave(settingsForm.getFieldsValue());
          }

          async function handleManualRefresh() {
            if (draftDirtyRef.current && typeof window !== "undefined" && typeof window.confirm === "function") {
              const confirmed = window.confirm("当前有未保存的修改。重新加载会覆盖左侧设置和公告草稿，是否继续？");
              if (!confirmed) {
                return;
              }
            }

            await refreshSettings({ source: "manual" });
          }

          function addAnnouncement() {
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
            markDraftDirty();
            setAnnouncements(function (current) {
              return current.map(function (item, itemIndex) {
                return itemIndex === index ? { ...item, ...patch } : item;
              });
            });
          }

          function moveAnnouncement(index, direction) {
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
                    h(Text, { type: "secondary", className: "page-brand-subtitle" }, "管理公开列表申请、显示名称和与公共服务器控制台的同步状态")
                  ),
                  h(
                    "div",
                    { className: "page-actions" },
                    h(Badge, { status: "processing", text: "已登录" }),
                    h(Button, { onClick: handleLogout }, "退出")
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
                              onFinish: handleSave,
                              onValuesChange: function () { markDraftDirty(); },
                              initialValues: {
                                displayName: settings.displayName || "",
                                publicListingEnabled: Boolean(settings.publicListingEnabled),
                                bandwidthCapacityMbps: settings.bandwidthCapacityMbps,
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
                              { name: "publicListingEnabled", label: "公开列表申请", valuePropName: "checked" },
                              h(Switch, { checkedChildren: "已启用", unCheckedChildren: "未启用" })
                            ),
                            h(
                              Form.Item,
                              { style: { marginBottom: 0 } },
                              h(
                                Space,
                                { size: 12 },
                                h(Button, { type: "primary", htmlType: "submit", loading: saveLoading }, "保存设置"),
                                h(Button, { onClick: function () { void handleManualRefresh(); }, loading: settingsLoading }, "重新加载配置")
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
                            { size: 8 },
                            h(Button, { onClick: function () { void saveAnnouncements(); }, loading: saveLoading }, "保存公告"),
                            h(Button, { type: "primary", onClick: addAnnouncement }, "新增公告")
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
                        h(Descriptions.Item, { label: "申请状态" }, renderListingStateTag(settings)),
                        h(Descriptions.Item, { label: "状态说明" }, getListingStateMeta(settings).description),
                        h(Descriptions.Item, { label: "同步状态" }, renderSyncTag(settings.lastSyncStatus || "idle")),
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
                        h(Descriptions.Item, { label: "容量来源" }, renderCapacitySource(settings.capacitySource || "unknown")),
                        h(Descriptions.Item, { label: "申请 ID" }, settings.submissionId || "未创建"),
                        h(Descriptions.Item, { label: "服务器 ID" }, settings.serverId || "未获批"),
                        h(Descriptions.Item, { label: "令牌状态" }, settings.hasServerToken ? h(Tag, { color: "success" }, "已签发") : h(Tag, null, "未签发")),
                        h(Descriptions.Item, { label: "最近同步" }, formatDateTime(settings.lastSyncAt)),
                        h(Descriptions.Item, { label: "最近错误" }, settings.lastSyncError || "无"),
                        h(Descriptions.Item, { label: "审核备注" }, settings.lastReviewNote || "无"),
                        h(Descriptions.Item, { label: "母面板地址" }, settings.registryBaseUrl || "未配置"),
                        h(Descriptions.Item, { label: "当前大厅地址" }, settings.publicBaseUrl || "未配置"),
                        h(Descriptions.Item, { label: "当前控制通道" }, settings.publicWsUrl || "未配置"),
                        h(Descriptions.Item, { label: "带宽探针地址" }, settings.bandwidthProbeUrl || "未配置")
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
