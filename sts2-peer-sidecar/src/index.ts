import express from "express";

async function main(): Promise<void> {
  const port = Number.parseInt(process.env.PEER_LISTEN_PORT ?? "18800", 10);
  const app = express();
  app.use(express.json());
  app.get("/healthz", (_req, res) => res.status(200).json({ ok: true }));
  app.listen(port, () => console.log(`[sidecar] listening on ${port}`));
}

main().catch((err) => { console.error(err); process.exit(1); });
