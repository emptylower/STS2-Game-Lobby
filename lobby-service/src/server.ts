import { loadLobbyServiceConfig } from "./config.js";
import { createLobbyService } from "./app.js";

const config = loadLobbyServiceConfig(process.env);

const service = await createLobbyService(config);
const address = await service.start();

console.log(`[lobby] listening on http://${address.host}:${address.port} (ws path ${config.wsPath})`);
console.log(
  `[relay] enabled udp://${config.relayBindHost}:${config.relayPortStart}-${config.relayPortEnd} publicHost=${config.relayPublicHost || "<request-host>"}`,
);
console.log(`[server-admin] panel ready at http://${address.host}:${address.port}/server-admin`);
if (config.serverAdminPasswordHash && config.serverAdminSessionSecret) {
  console.log(`[server-admin] login enabled for ${config.serverAdminUsername}`);
} else {
  console.log(
    "[server-admin] login disabled until SERVER_ADMIN_PASSWORD_HASH and SERVER_ADMIN_SESSION_SECRET are configured",
  );
}

console.log("[server-admin] decentralized listing mode: toggle via admin panel");

let shuttingDown = false;
async function shutdown(signal: string) {
  if (shuttingDown) {
    return;
  }
  shuttingDown = true;
  console.log(`[lobby] received ${signal}, shutting down`);
  try {
    await service.close();
    process.exit(0);
  } catch (error) {
    console.error("[lobby] shutdown failed", error);
    process.exit(1);
  }
}

process.on("SIGINT", () => {
  void shutdown("SIGINT");
});
process.on("SIGTERM", () => {
  void shutdown("SIGTERM");
});
