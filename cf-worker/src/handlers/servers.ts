import type { Env } from "../index.js";
import { KV_KEY_ACTIVE, type ActiveServersDocument } from "../types.js";

const FALLBACK: ActiveServersDocument = {
  version: 1,
  updated_at: new Date(0).toISOString(),
  servers: [],
};

export async function handleGetServers(env: Env): Promise<Response> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_ACTIVE);
  const body: ActiveServersDocument = raw ? (JSON.parse(raw) as ActiveServersDocument) : FALLBACK;
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "public, max-age=60",
    },
  });
}
