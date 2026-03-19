export function renderAdminPage() {
  return `<!doctype html>
<html lang="zh-CN">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>STS2 服务器目录后台</title>
    <style>
      :root {
        color-scheme: light;
        --bg: #f4f0e6;
        --panel: rgba(255, 252, 246, 0.92);
        --text: #1f1a17;
        --muted: #665c4d;
        --accent: #986c18;
        --border: rgba(152, 108, 24, 0.22);
        --danger: #b34f3d;
        --success: #3b7d52;
      }
      * { box-sizing: border-box; }
      body {
        margin: 0;
        font-family: "Iowan Old Style", "Palatino Linotype", "Source Han Serif SC", serif;
        color: var(--text);
        background:
          radial-gradient(circle at top left, rgba(152,108,24,0.18), transparent 32%),
          linear-gradient(180deg, #f8f4eb 0%, #efe4d2 100%);
      }
      main {
        max-width: 1180px;
        margin: 0 auto;
        padding: 32px 20px 80px;
      }
      h1, h2, h3 { margin: 0; font-weight: 600; }
      p { margin: 0; color: var(--muted); }
      .hero {
        display: grid;
        gap: 12px;
        margin-bottom: 20px;
      }
      .hero h1 { font-size: 32px; }
      .grid {
        display: grid;
        grid-template-columns: 340px minmax(0, 1fr);
        gap: 18px;
      }
      .panel {
        background: var(--panel);
        border: 1px solid var(--border);
        border-radius: 20px;
        padding: 18px;
        box-shadow: 0 16px 32px rgba(61, 46, 20, 0.08);
      }
      .panel + .panel { margin-top: 18px; }
      .stack { display: grid; gap: 12px; }
      label { display: grid; gap: 6px; font-size: 14px; color: var(--muted); }
      input, textarea, select, button {
        font: inherit;
        border-radius: 12px;
        border: 1px solid rgba(102, 92, 77, 0.24);
        padding: 10px 12px;
        background: rgba(255,255,255,0.88);
      }
      textarea { min-height: 90px; resize: vertical; }
      button {
        cursor: pointer;
        background: #2b241d;
        color: #fbf5e8;
        border: none;
      }
      button.secondary {
        background: #e9ddc7;
        color: var(--text);
      }
      button.danger { background: var(--danger); }
      button.success { background: var(--success); }
      button:disabled { cursor: default; opacity: 0.6; }
      .actions { display: flex; flex-wrap: wrap; gap: 8px; }
      .badge {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 4px 10px;
        border-radius: 999px;
        background: rgba(152, 108, 24, 0.12);
        color: var(--accent);
        font-size: 12px;
      }
      .muted { color: var(--muted); }
      .danger-text { color: var(--danger); }
      .success-text { color: var(--success); }
      table {
        width: 100%;
        border-collapse: collapse;
        font-size: 14px;
      }
      th, td {
        text-align: left;
        padding: 10px 8px;
        border-bottom: 1px solid rgba(102, 92, 77, 0.12);
        vertical-align: top;
      }
      th { color: var(--muted); font-weight: 600; }
      .hidden { display: none !important; }
      .item-meta { display: grid; gap: 4px; }
      .toolbar {
        display: flex;
        justify-content: space-between;
        gap: 12px;
        align-items: center;
        margin-bottom: 12px;
      }
      .login-note {
        font-size: 13px;
        line-height: 1.5;
      }
      @media (max-width: 960px) {
        .grid { grid-template-columns: 1fr; }
      }
    </style>
  </head>
  <body>
    <main>
      <section class="hero">
        <span class="badge">STS2 中央注册表 / 审核后台</span>
        <h1>大厅服务器目录控制台</h1>
        <p>审核社区服务器提交、手动复检服务器状态，并控制上架、禁用或维护状态。</p>
      </section>
      <section class="grid">
        <aside>
          <div class="panel stack" id="loginPanel">
            <h2>管理员登录</h2>
            <p class="login-note">当前后台使用单管理员账号模式。登录成功后可审核提交、修改服务器状态并触发探针复检。</p>
            <label>用户名<input id="username" autocomplete="username" /></label>
            <label>密码<input id="password" type="password" autocomplete="current-password" /></label>
            <div class="actions">
              <button id="loginButton">登录</button>
            </div>
            <p id="loginStatus" class="muted"></p>
          </div>
          <div class="panel stack hidden" id="sessionPanel">
            <div class="toolbar">
              <h2>当前会话</h2>
              <button class="secondary" id="logoutButton">退出</button>
            </div>
            <p id="sessionSummary"></p>
            <p class="muted">后台接口全部走同源 cookie session，不需要额外 token。</p>
          </div>
        </aside>
        <section class="stack">
          <div class="panel hidden" id="dashboardPanel">
            <div class="toolbar">
              <div class="stack">
                <h2>待审核提交</h2>
                <p>审核通过后会直接转成“社区已审核”服务器条目。</p>
              </div>
              <button class="secondary" id="refreshSubmissions">刷新</button>
            </div>
            <div id="submissionTable" class="muted">登录后加载。</div>
          </div>
          <div class="panel hidden" id="serversPanel">
            <div class="toolbar">
              <div class="stack">
                <h2>服务器目录</h2>
                <p>可手动切换上架状态、维护状态、备注，并触发即时复检。</p>
              </div>
              <button class="secondary" id="refreshServers">刷新</button>
            </div>
            <div id="serverTable" class="muted">登录后加载。</div>
          </div>
        </section>
      </section>
    </main>
    <script>
      const loginPanel = document.getElementById("loginPanel");
      const sessionPanel = document.getElementById("sessionPanel");
      const dashboardPanel = document.getElementById("dashboardPanel");
      const serversPanel = document.getElementById("serversPanel");
      const loginStatus = document.getElementById("loginStatus");
      const sessionSummary = document.getElementById("sessionSummary");

      async function readJson(path, options) {
        const response = await fetch(path, options);
        const text = await response.text();
        const payload = text ? JSON.parse(text) : {};
        if (!response.ok) {
          throw new Error(payload.message || response.statusText || "请求失败");
        }
        return payload;
      }

      async function refreshSession() {
        try {
          const session = await readJson("/admin/session");
          loginPanel.classList.add("hidden");
          sessionPanel.classList.remove("hidden");
          dashboardPanel.classList.remove("hidden");
          serversPanel.classList.remove("hidden");
          sessionSummary.textContent = "已登录：" + session.username + "，过期时间：" + new Date(session.expiresAt).toLocaleString();
          await Promise.all([refreshSubmissions(), refreshServers()]);
        } catch {
          loginPanel.classList.remove("hidden");
          sessionPanel.classList.add("hidden");
          dashboardPanel.classList.add("hidden");
          serversPanel.classList.add("hidden");
          sessionSummary.textContent = "";
        }
      }

      async function refreshSubmissions() {
        const container = document.getElementById("submissionTable");
        container.textContent = "正在加载…";
        try {
          const submissions = await readJson("/admin/submissions");
          if (!submissions.length) {
            container.innerHTML = "<p class='muted'>当前没有待审核或历史提交流水。</p>";
            return;
          }

          container.innerHTML = "<table><thead><tr><th>服务器</th><th>联系方式</th><th>状态</th><th>操作</th></tr></thead><tbody>" +
            submissions.map((entry) => {
              const pending = entry.status === "pending";
              return "<tr>" +
                "<td><div class='item-meta'><strong>" + escapeHtml(entry.displayName) + "</strong><span class='muted'>" + escapeHtml(entry.regionLabel) + "</span><span class='muted'>" + escapeHtml(entry.baseUrl) + "</span></div></td>" +
                "<td><div class='item-meta'><span>" + escapeHtml(entry.operatorName) + "</span><span class='muted'>" + escapeHtml(entry.contact) + "</span></div></td>" +
                "<td><div class='item-meta'><span>" + escapeHtml(entry.status) + "</span><span class='muted'>" + escapeHtml(entry.reviewNote || "") + "</span></div></td>" +
                "<td><div class='actions'>" +
                  (pending ? "<button class='success' data-action='approve' data-id='" + escapeHtml(entry.id) + "'>通过</button><button class='danger' data-action='reject' data-id='" + escapeHtml(entry.id) + "'>拒绝</button>" : "<span class='muted'>已处理</span>") +
                "</div></td>" +
              "</tr>";
            }).join("") +
            "</tbody></table>";
        } catch (error) {
          container.innerHTML = "<p class='danger-text'>" + escapeHtml(error.message) + "</p>";
        }
      }

      async function refreshServers() {
        const container = document.getElementById("serverTable");
        container.textContent = "正在加载…";
        try {
          const servers = await readJson("/admin/servers");
          container.innerHTML = "<table><thead><tr><th>服务器</th><th>状态</th><th>质量</th><th>操作</th></tr></thead><tbody>" +
            servers.map((entry) =>
              "<tr>" +
                "<td><div class='item-meta'><strong>" + escapeHtml(entry.displayName) + "</strong><span class='muted'>" + escapeHtml(entry.regionLabel) + " · " + escapeHtml(entry.sourceType) + "</span><span class='muted'>" + escapeHtml(entry.baseUrl) + "</span></div></td>" +
                "<td><div class='item-meta'><span>" + escapeHtml(entry.listingState) + " / " + escapeHtml(entry.runtimeState) + "</span><span class='muted'>" + escapeHtml(entry.failureReason || "") + "</span></div></td>" +
                "<td><div class='item-meta'><span>" + escapeHtml(entry.qualityGrade) + "</span><span class='muted'>" + (entry.lastProbeRttMs != null ? "RTT " + entry.lastProbeRttMs + "ms" : "未探测") + "</span></div></td>" +
                "<td><div class='actions'>" +
                  "<button class='secondary' data-action='probe' data-id='" + escapeHtml(entry.id) + "'>复检</button>" +
                  "<button class='secondary' data-action='maintenance' data-id='" + escapeHtml(entry.id) + "'>维护中</button>" +
                  "<button class='secondary' data-action='enable' data-id='" + escapeHtml(entry.id) + "'>上架</button>" +
                  "<button class='danger' data-action='disable' data-id='" + escapeHtml(entry.id) + "'>禁用</button>" +
                "</div></td>" +
              "</tr>"
            ).join("") +
            "</tbody></table>";
        } catch (error) {
          container.innerHTML = "<p class='danger-text'>" + escapeHtml(error.message) + "</p>";
        }
      }

      document.getElementById("loginButton").addEventListener("click", async () => {
        const username = document.getElementById("username").value.trim();
        const password = document.getElementById("password").value;
        loginStatus.textContent = "登录中…";
        try {
          await readJson("/admin/login", {
            method: "POST",
            headers: { "content-type": "application/json" },
            body: JSON.stringify({ username, password }),
          });
          loginStatus.textContent = "";
          document.getElementById("password").value = "";
          await refreshSession();
        } catch (error) {
          loginStatus.textContent = error.message;
        }
      });

      document.getElementById("logoutButton").addEventListener("click", async () => {
        await fetch("/admin/logout", { method: "POST" });
        await refreshSession();
      });

      document.getElementById("refreshSubmissions").addEventListener("click", refreshSubmissions);
      document.getElementById("refreshServers").addEventListener("click", refreshServers);

      document.addEventListener("click", async (event) => {
        const button = event.target.closest("button[data-action]");
        if (!button) {
          return;
        }

        const { action, id } = button.dataset;
        button.disabled = true;
        try {
          if (action === "approve") {
            await readJson("/admin/submissions/" + id + "/approve", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ note: "通过后台审核" }) });
            await Promise.all([refreshSubmissions(), refreshServers()]);
          } else if (action === "reject") {
            await readJson("/admin/submissions/" + id + "/reject", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ note: "未通过后台审核" }) });
            await refreshSubmissions();
          } else if (action === "probe") {
            await readJson("/admin/servers/" + id + "/probe", { method: "POST" });
            await refreshServers();
          } else if (action === "maintenance") {
            await readJson("/admin/servers/" + id, { method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify({ runtimeState: "maintenance" }) });
            await refreshServers();
          } else if (action === "enable") {
            await readJson("/admin/servers/" + id, { method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify({ listingState: "approved", runtimeState: "online" }) });
            await refreshServers();
          } else if (action === "disable") {
            await readJson("/admin/servers/" + id, { method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify({ listingState: "disabled" }) });
            await refreshServers();
          }
        } catch (error) {
          alert(error.message);
        } finally {
          button.disabled = false;
        }
      });

      function escapeHtml(value) {
        return String(value)
          .replaceAll("&", "&amp;")
          .replaceAll("<", "&lt;")
          .replaceAll(">", "&gt;")
          .replaceAll('"', "&quot;")
          .replaceAll("'", "&#39;");
      }

      refreshSession();
    </script>
  </body>
</html>`;
}
