export function renderAdminPage() {
  return `<!doctype html>
<html lang="zh-CN">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>STS2 公共服务器控制台</title>
    <link rel="stylesheet" href="https://unpkg.com/antd@5.22.6/dist/reset.css" />
    <style>
      html, body, #root { min-height: 100%; }
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
        background: rgba(255,255,255,0.86);
        backdrop-filter: blur(10px);
        border-bottom: 1px solid rgba(5, 5, 5, 0.06);
      }
      .page-header-inner {
        max-width: 1380px;
        margin: 0 auto;
        padding: 20px 24px;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 16px;
      }
      .page-brand {
        display: flex;
        flex-direction: column;
        gap: 4px;
      }
      .page-content {
        max-width: 1380px;
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
      .console-card,
      .list-card {
        border-radius: 18px;
        box-shadow: 0 16px 36px rgba(15, 23, 42, 0.08);
      }
      .session-block .ant-descriptions-item-label {
        width: 150px;
        color: #667085;
      }
      .item-meta {
        display: grid;
        gap: 8px;
      }
      .item-meta-line {
        display: flex;
        flex-wrap: wrap;
        gap: 12px;
      }
      .item-actions {
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
        justify-content: flex-end;
      }
      .item-grid {
        display: grid;
        grid-template-columns: minmax(280px, 1.4fr) minmax(180px, 0.9fr) minmax(220px, 1fr) auto;
        gap: 16px;
        align-items: start;
      }
      .stack-list {
        display: grid;
        gap: 12px;
      }
      @media (max-width: 1180px) {
        .item-grid {
          grid-template-columns: 1fr;
        }
        .item-actions {
          justify-content: flex-start;
        }
      }
      @media (max-width: 992px) {
        .page-header-inner,
        .page-content {
          padding-left: 16px;
          padding-right: 16px;
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
          Badge,
          Button,
          Card,
          Col,
          ConfigProvider,
          Descriptions,
          Empty,
          Form,
          Input,
          Layout,
          Row,
          Space,
          Spin,
          Tag,
          Typography,
          message,
        } = antd;
        const { Title, Paragraph, Text } = Typography;

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

        function renderStatusTag(value) {
          const mapping = {
            pending: ["warning", "待审核"],
            approved: ["success", "已通过"],
            rejected: ["error", "已拒绝"],
            online: ["success", "在线"],
            degraded: ["warning", "降级"],
            offline: ["error", "离线"],
            maintenance: ["processing", "维护中"],
            excellent: ["gold", "优秀"],
            good: ["green", "良好"],
            fair: ["blue", "一般"],
            poor: ["red", "较差"],
            disabled: ["default", "已下架"],
          };
          const item = mapping[value] || ["default", value || "未知"];
          return h(Tag, { color: item[0] }, item[1]);
        }

        function renderGuardTag(value) {
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
          return typeof value === "number" ? value.toFixed(2) + " Mbps" : "-";
        }

        function formatRatio(value) {
          return typeof value === "number" ? (value * 100).toFixed(1) + "%" : "-";
        }

        function App() {
          const [booting, setBooting] = React.useState(true);
          const [session, setSession] = React.useState(null);
          const [submissions, setSubmissions] = React.useState([]);
          const [servers, setServers] = React.useState([]);
          const [loginLoading, setLoginLoading] = React.useState(false);
          const [submissionsLoading, setSubmissionsLoading] = React.useState(false);
          const [serversLoading, setServersLoading] = React.useState(false);
          const [form] = Form.useForm();

          const refreshSubmissions = React.useCallback(async function () {
            setSubmissionsLoading(true);
            try {
              const next = await readJson("/admin/submissions");
              setSubmissions(next);
            } finally {
              setSubmissionsLoading(false);
            }
          }, []);

          const refreshServers = React.useCallback(async function () {
            setServersLoading(true);
            try {
              const next = await readJson("/admin/servers");
              setServers(next);
            } finally {
              setServersLoading(false);
            }
          }, []);

          const refreshSession = React.useCallback(async function () {
            setBooting(true);
            try {
              const nextSession = await readJson("/admin/session");
              setSession(nextSession);
              await Promise.all([refreshSubmissions(), refreshServers()]);
            } catch (_error) {
              setSession(null);
              setSubmissions([]);
              setServers([]);
            } finally {
              setBooting(false);
            }
          }, [refreshServers, refreshSubmissions]);

          React.useEffect(function () {
            void refreshSession();
          }, [refreshSession]);

          async function handleLogin(values) {
            setLoginLoading(true);
            try {
              await readJson("/admin/login", {
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
            await fetch("/admin/logout", { method: "POST" });
            form.resetFields();
            setSession(null);
            setSubmissions([]);
            setServers([]);
            message.success("已退出登录");
          }

          async function handleSubmissionAction(action, id) {
            try {
              await readJson("/admin/submissions/" + id + "/" + action, {
                method: "POST",
                headers: { "content-type": "application/json" },
                body: JSON.stringify({
                  note: action === "approve" ? "通过后台审核" : "未通过后台审核",
                }),
              });
              message.success(action === "approve" ? "申请已通过" : "申请已拒绝");
              await Promise.all([refreshSubmissions(), refreshServers()]);
            } catch (error) {
              message.error(error.message || "操作失败");
            }
          }

          async function handleServerAction(id, action) {
            try {
              if (action === "probe") {
                await readJson("/admin/servers/" + id + "/probe", { method: "POST" });
              } else {
                const payload = action === "enable"
                  ? { listingState: "approved", runtimeState: "online" }
                  : action === "maintenance"
                    ? { runtimeState: "maintenance" }
                    : { listingState: "disabled" };
                await readJson("/admin/servers/" + id, {
                  method: "PATCH",
                  headers: { "content-type": "application/json" },
                  body: JSON.stringify(payload),
                });
              }
              message.success("操作已执行");
              await refreshServers();
            } catch (error) {
              message.error(error.message || "操作失败");
            }
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
                    h(Title, { level: 2, style: { marginTop: 0, marginBottom: 8 } }, "公共服务器控制台"),
                    h(Paragraph, { type: "secondary", style: { marginBottom: 24 } }, "登录后审核服务器申请，查看运行状态、探针结果并维护公开服务器目录。"),
                    h(
                      Form,
                      { form: form, layout: "vertical", onFinish: handleLogin },
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
                    h(Title, { level: 3, style: { margin: 0 } }, "公共服务器控制台"),
                    h(Text, { type: "secondary" }, "审核申请、查看探针与心跳状态，并维护公共服务器目录")
                  ),
                  h(
                    Space,
                    { size: 12 },
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
                    { xs: 24, lg: 6 },
                    h(
                      Card,
                      { className: "console-card", title: "当前会话" },
                      h(
                        Descriptions,
                        { bordered: true, size: "middle", column: 1, className: "session-block" },
                        h(Descriptions.Item, { label: "登录用户" }, session.username),
                        h(Descriptions.Item, { label: "过期时间" }, formatDateTime(session.expiresAt)),
                        h(Descriptions.Item, { label: "待审核申请" }, submissions.filter(function (item) { return item.status === "pending"; }).length),
                        h(Descriptions.Item, { label: "在线服务器" }, servers.filter(function (item) { return item.runtimeState === "online"; }).length)
                      )
                    )
                  ),
                  h(
                    Col,
                    { xs: 24, lg: 18 },
                    h(
                      Space,
                      { direction: "vertical", size: 16, style: { width: "100%" } },
                      h(
                        Card,
                        {
                          className: "console-card",
                          title: h("div", { className: "section-title" }, h("span", null, "待审核申请")),
                          extra: h(Button, { onClick: function () { void refreshSubmissions(); }, loading: submissionsLoading }, "刷新"),
                        },
                        submissions.length === 0
                          ? h(Empty, { description: "当前没有申请记录" })
                          : h(
                              "div",
                              { className: "stack-list" },
                              submissions.map(function (record) {
                                return h(
                                  Card,
                                  { key: record.id, size: "small", className: "list-card" },
                                  h(
                                    "div",
                                    { className: "item-grid" },
                                    h(
                                      "div",
                                      { className: "item-meta" },
                                      h(Text, { strong: true }, record.displayName),
                                      h(Text, { type: "secondary", copyable: { text: record.baseUrl } }, record.baseUrl),
                                      h(Text, { type: "secondary" }, "提交时间： " + formatDateTime(record.submittedAt)),
                                      record.reviewNote ? h(Text, { type: "secondary" }, "备注： " + record.reviewNote) : null
                                    ),
                                    h("div", { className: "item-meta" }, h(Text, { type: "secondary" }, "状态"), renderStatusTag(record.status)),
                                    h("div", { className: "item-meta" }, h(Text, { type: "secondary" }, "来源"), h(Text, null, "IP： " + (record.sourceIp || "<unknown>"))),
                                    h(
                                      "div",
                                      { className: "item-actions" },
                                      record.status === "pending"
                                        ? [
                                            h(Button, { key: "approve", type: "primary", onClick: function () { void handleSubmissionAction("approve", record.id); } }, "通过"),
                                            h(Button, { key: "reject", danger: true, onClick: function () { void handleSubmissionAction("reject", record.id); } }, "拒绝"),
                                          ]
                                        : h(Text, { type: "secondary" }, "已处理")
                                    )
                                  )
                                );
                              })
                            )
                      ),
                      h(
                        Card,
                        {
                          className: "console-card",
                          title: h("div", { className: "section-title" }, h("span", null, "服务器目录")),
                          extra: h(Button, { onClick: function () { void refreshServers(); }, loading: serversLoading }, "刷新"),
                        },
                        servers.length === 0
                          ? h(Empty, { description: "当前没有服务器记录" })
                          : h(
                              "div",
                              { className: "stack-list" },
                              servers.map(function (record) {
                                return h(
                                  Card,
                                  { key: record.id, size: "small", className: "list-card" },
                                  h(
                                    "div",
                                    { className: "item-grid" },
                                    h(
                                      "div",
                                      { className: "item-meta" },
                                      h(Text, { strong: true }, record.displayName),
                                      h(Text, { type: "secondary", copyable: { text: record.baseUrl } }, record.baseUrl),
                                      h(
                                        "div",
                                        { className: "item-meta-line" },
                                        h(Text, { type: "secondary" }, "房间数： " + record.roomCount),
                                        h(Text, { type: "secondary" }, "公开： " + (record.publicListingEnabled ? "是" : "否")),
                                        h(Text, { type: "secondary" }, "上次心跳： " + formatDateTime(record.lastHeartbeatAt))
                                      )
                                    ),
                                    h(
                                      "div",
                                      { className: "item-meta" },
                                      h(Text, { type: "secondary" }, "状态"),
                                      renderStatusTag(record.runtimeState),
                                      h(Text, { type: "secondary" }, "listing = " + record.listingState),
                                      h(Text, { type: "secondary" }, "最近探针： " + formatDateTime(record.lastProbeAt))
                                    ),
                                    h(
                                      "div",
                                      { className: "item-meta" },
                                      h(Text, { type: "secondary" }, "质量"),
                                      renderStatusTag(record.qualityGrade),
                                      h(Text, { type: "secondary" }, "RTT： " + (record.lastProbeRttMs == null ? "-" : record.lastProbeRttMs + " ms")),
                                      h(Text, { type: "secondary" }, "探针带宽： " + formatMbps(record.lastBandwidthMbps)),
                                      h(Text, { type: "secondary" }, "实时带宽： " + formatMbps(record.currentBandwidthMbps)),
                                      h(Text, { type: "secondary" }, "有效容量： " + formatMbps(record.resolvedCapacityMbps)),
                                      h(Text, { type: "secondary" }, "利用率： " + formatRatio(record.bandwidthUtilizationRatio)),
                                      renderCapacitySource(record.capacitySource),
                                      renderGuardTag(record.createRoomGuardStatus)
                                    ),
                                    h(
                                      "div",
                                      { className: "item-actions" },
                                      h(Button, { onClick: function () { void handleServerAction(record.id, "probe"); } }, "复检"),
                                      h(Button, { onClick: function () { void handleServerAction(record.id, "enable"); } }, "上架"),
                                      h(Button, { onClick: function () { void handleServerAction(record.id, "maintenance"); } }, "维护"),
                                      h(Button, { danger: true, onClick: function () { void handleServerAction(record.id, "disable"); } }, "下架")
                                    )
                                  )
                                );
                              })
                            )
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
