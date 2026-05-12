const TIMEOUT_MS = 5_000;

export interface SeedAddress { address: string; note?: string; }

export async function loadSeedsFromCf(cfBaseUrl: string): Promise<SeedAddress[]> {
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), TIMEOUT_MS);
  try {
    const url = `${cfBaseUrl.replace(/\/+$/, "")}/v1/seeds`;
    const res = await fetch(url, { signal: ctrl.signal });
    if (!res.ok) return [];
    const body = (await res.json()) as { seeds?: SeedAddress[] };
    return body.seeds ?? [];
  } catch { return []; } finally { clearTimeout(t); }
}
