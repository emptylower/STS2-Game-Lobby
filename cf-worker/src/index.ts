import { handleGetSeeds } from "./handlers/seeds.js";
import { handleGetServers } from "./handlers/servers.js";

export interface Env {
  DISCOVERY_KV: KVNamespace;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/v1/seeds") {
      return handleGetSeeds(env);
    }
    if (request.method === "GET" && url.pathname === "/v1/servers") {
      return handleGetServers(env);
    }
    return new Response("not found", { status: 404 });
  },
  async scheduled(_event: ScheduledEvent, _env: Env, _ctx: ExecutionContext): Promise<void> {},
};
