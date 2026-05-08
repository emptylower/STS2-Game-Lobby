import type { Env } from "../index.js";
import { KV_KEY_ANNOUNCEMENTS, type AnnouncementsDocument } from "../types.js";

const FALLBACK: AnnouncementsDocument = {
  version: 1,
  updated_at: new Date(0).toISOString(),
  items: [],
};

export async function handleGetAnnouncements(env: Env): Promise<Response> {
  const raw = await env.DISCOVERY_KV.get(KV_KEY_ANNOUNCEMENTS);
  const body: AnnouncementsDocument = raw ? (JSON.parse(raw) as AnnouncementsDocument) : FALLBACK;
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "public, max-age=300",
    },
  });
}
