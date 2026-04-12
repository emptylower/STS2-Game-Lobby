export interface RollingBandwidthSnapshot {
  currentBandwidthMbps: number;
  totalBytesInWindow: number;
  windowMs: number;
}

interface RollingBandwidthSample {
  at: number;
  bytes: number;
}

export class RollingBandwidthMeter {
  private readonly windowMs: number;
  private readonly samples: RollingBandwidthSample[] = [];
  private headIndex = 0;
  private bytesInWindow = 0;

  constructor(windowMs = 30_000) {
    this.windowMs = Math.max(1_000, windowMs);
  }

  recordBytes(bytes: number, now = Date.now()) {
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return;
    }

    this.prune(now);
    this.samples.push({
      at: now,
      bytes,
    });
    this.bytesInWindow += bytes;
  }

  getSnapshot(now = Date.now()): RollingBandwidthSnapshot {
    this.prune(now);
    return {
      currentBandwidthMbps: roundMbps((this.bytesInWindow * 8) / (this.windowMs / 1000) / 1_000_000),
      totalBytesInWindow: this.bytesInWindow,
      windowMs: this.windowMs,
    };
  }

  private prune(now: number) {
    const cutoff = now - this.windowMs;
    while (this.headIndex < this.samples.length && this.samples[this.headIndex]!.at < cutoff) {
      this.bytesInWindow -= this.samples[this.headIndex]!.bytes;
      this.headIndex += 1;
    }

    if (this.headIndex > 256 && this.headIndex >= Math.floor(this.samples.length / 2)) {
      this.samples.splice(0, this.headIndex);
      this.headIndex = 0;
    }

    if (this.bytesInWindow < 0) {
      this.bytesInWindow = 0;
    }
  }
}

function roundMbps(value: number) {
  return Math.round(value * 100) / 100;
}
