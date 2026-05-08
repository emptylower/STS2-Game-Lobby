export interface Env {
  DISCOVERY_KV: KVNamespace;
}

export default {
  async fetch(_request: Request, _env: Env): Promise<Response> {
    return new Response("sts2-discovery worker placeholder", { status: 200 });
  },
  async scheduled(_event: ScheduledEvent, _env: Env, _ctx: ExecutionContext): Promise<void> {
    // cron placeholder
  },
};
