import type { Env } from "../index.js";
import { KV_KEY_SEEDS, type SeedsDocument } from "../types.js";

const FALLBACK: SeedsDocument = {
  version: 1,
  updated_at: new Date(0).toISOString(),
  seeds: [],
};

export async function handleGetSeeds(env: Env): Promise<Response> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_SEEDS);
  const body: SeedsDocument = raw ? (JSON.parse(raw) as SeedsDocument) : FALLBACK;
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "public, max-age=3600",
    },
  });
}
