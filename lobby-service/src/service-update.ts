import { execFile } from "node:child_process";
import { access, mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import { constants as fsConstants, existsSync } from "node:fs";
import { dirname, join, resolve, sep } from "node:path";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const DefaultReleaseApiUrl = "https://api.github.com/repos/emptylower/STS2-Game-Lobby/releases?per_page=20";
const ServiceAssetName = "sts2_lobby_service.zip";
const MaxArchiveBytes = 128 * 1024 * 1024;

export type ServiceDeploymentMode = "docker" | "systemd" | "unknown";
export type ServiceUpdatePhase =
  | "idle"
  | "checking"
  | "available"
  | "downloading"
  | "installing"
  | "restarting"
  | "up_to_date"
  | "failed";

export interface ServiceUpdateStatus {
  currentVersion: string;
  latestVersion: string | null;
  updateAvailable: boolean;
  phase: ServiceUpdatePhase;
  deploymentMode: ServiceDeploymentMode;
  enabled: boolean;
  canUpdate: boolean;
  preflight: string[];
  releaseUrl: string | null;
  releaseNotes: string | null;
  checkedAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  error: string | null;
}

interface GithubReleaseAsset {
  name?: unknown;
  browser_download_url?: unknown;
}

interface GithubReleasePayload {
  tag_name?: unknown;
  html_url?: unknown;
  body?: unknown;
  draft?: unknown;
  prerelease?: unknown;
  assets?: unknown;
}

interface ResolvedRelease {
  version: string;
  assetUrl: string;
  releaseUrl: string;
  notes: string;
}

export interface ServiceUpdateManagerOptions {
  currentVersion: string;
  enabled: boolean;
  dataDir: string;
  deploymentMode?: ServiceDeploymentMode;
  releaseApiUrl?: string;
  checkIntervalMs?: number;
  restart?: () => void;
  fetchImpl?: typeof fetch;
}

export function normalizeServiceVersion(value: string): string | null {
  const match = /^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(value.trim());
  return match ? `${match[1]}.${match[2]}.${match[3]}` : null;
}

export function compareServiceVersions(left: string, right: string): number {
  const leftVersion = normalizeServiceVersion(left);
  const rightVersion = normalizeServiceVersion(right);
  if (!leftVersion || !rightVersion) {
    throw new Error("服务版本必须使用 x.y.z 格式。");
  }
  const leftParts = leftVersion.split(".").map(Number);
  const rightParts = rightVersion.split(".").map(Number);
  for (let index = 0; index < 3; index += 1) {
    const difference = leftParts[index]! - rightParts[index]!;
    if (difference !== 0) return difference;
  }
  return 0;
}

export function detectServiceDeploymentMode(source: NodeJS.ProcessEnv = process.env): ServiceDeploymentMode {
  const configured = source.SERVER_UPDATE_DEPLOYMENT_MODE?.trim().toLowerCase();
  if (configured === "docker" || configured === "systemd") return configured;
  if (source.STS2_CONTAINER === "true" || existsSync("/.dockerenv")) return "docker";
  if (source.INVOCATION_ID || source.JOURNAL_STREAM) return "systemd";
  return "unknown";
}

export function validateServiceArchiveEntries(entries: string[]): void {
  if (entries.length === 0) throw new Error("更新包为空。");
  let hasPackageJson = false;
  for (const entry of entries) {
    if (!entry || entry.includes("\\") || entry.startsWith("/") || entry.includes("\0")) {
      throw new Error(`更新包包含不安全路径：${entry}`);
    }
    const parts = entry.split("/").filter(Boolean);
    if (parts.includes("..") || parts[0] !== "sts2_lobby_service") {
      throw new Error(`更新包路径超出服务目录：${entry}`);
    }
    if (entry === "sts2_lobby_service/lobby-service/package.json") hasPackageJson = true;
  }
  if (!hasPackageJson) throw new Error("更新包缺少 lobby-service/package.json。 ");
}

export function parseGithubServiceRelease(payload: GithubReleasePayload): ResolvedRelease {
  if (payload.draft === true || payload.prerelease === true) {
    throw new Error("GitHub 最新 Release 不是稳定版本。");
  }
  const version = typeof payload.tag_name === "string" ? normalizeServiceVersion(payload.tag_name) : null;
  if (!version) throw new Error("GitHub Release 标签不是有效的服务版本。");
  const assets = Array.isArray(payload.assets) ? payload.assets as GithubReleaseAsset[] : [];
  const asset = assets.find((candidate) => candidate.name === ServiceAssetName);
  if (!asset || typeof asset.browser_download_url !== "string") {
    throw new Error(`GitHub Release 缺少 ${ServiceAssetName}。`);
  }
  return {
    version,
    assetUrl: asset.browser_download_url,
    releaseUrl: typeof payload.html_url === "string" ? payload.html_url : "",
    notes: typeof payload.body === "string" ? payload.body.slice(0, 4000) : "",
  };
}

export function selectLatestGithubServiceRelease(payload: unknown): ResolvedRelease {
  if (!Array.isArray(payload)) throw new Error("GitHub Release 列表格式无效。");
  for (const candidate of payload as GithubReleasePayload[]) {
    if (candidate.draft === true || candidate.prerelease === true) continue;
    const assets = Array.isArray(candidate.assets) ? candidate.assets as GithubReleaseAsset[] : [];
    if (!assets.some((asset) => asset.name === ServiceAssetName)) continue;
    try {
      return parseGithubServiceRelease(candidate);
    } catch {
      // Skip malformed entries and continue to the next stable service release.
    }
  }
  throw new Error(`最近的稳定 Release 中没有 ${ServiceAssetName}。`);
}

export class ServiceUpdateManager {
  private readonly currentVersion: string;
  private readonly dataDir: string;
  private readonly releaseApiUrl: string;
  private readonly deploymentMode: ServiceDeploymentMode;
  private readonly enabled: boolean;
  private readonly checkIntervalMs: number;
  private readonly restart: () => void;
  private readonly fetchImpl: typeof fetch;
  private timer: NodeJS.Timeout | null = null;
  private startupTimer: NodeJS.Timeout | null = null;
  private operation: Promise<ServiceUpdateStatus> | null = null;
  private release: ResolvedRelease | null = null;
  private status: ServiceUpdateStatus;

  constructor(options: ServiceUpdateManagerOptions) {
    this.currentVersion = options.currentVersion;
    this.dataDir = resolve(options.dataDir);
    this.releaseApiUrl = options.releaseApiUrl ?? DefaultReleaseApiUrl;
    this.deploymentMode = options.deploymentMode ?? detectServiceDeploymentMode();
    this.enabled = options.enabled;
    this.checkIntervalMs = options.checkIntervalMs ?? 6 * 60 * 60 * 1000;
    this.restart = options.restart ?? (() => process.exit(75));
    this.fetchImpl = options.fetchImpl ?? fetch;
    this.status = {
      currentVersion: this.currentVersion,
      latestVersion: null,
      updateAvailable: false,
      phase: "idle",
      deploymentMode: this.deploymentMode,
      enabled: this.enabled,
      canUpdate: false,
      preflight: [],
      releaseUrl: null,
      releaseNotes: null,
      checkedAt: null,
      startedAt: null,
      completedAt: null,
      error: null,
    };
  }

  start(): void {
    if (!this.enabled || this.timer) return;
    if (process.env.STS2_RUNTIME_LAUNCHER === "1") {
      this.startupTimer = setTimeout(() => {
        this.startupTimer = null;
        void this.check().catch((error) => {
          console.error("[service-update] startup check failed", error);
        });
      }, 5000);
      this.startupTimer.unref();
    }
    this.timer = setInterval(() => {
      void this.check().catch((error) => {
        console.error("[service-update] automatic check failed", error);
      });
    }, this.checkIntervalMs);
    this.timer.unref();
  }

  stop(): void {
    if (this.startupTimer) clearTimeout(this.startupTimer);
    this.startupTimer = null;
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }

  getStatus(): ServiceUpdateStatus {
    return { ...this.status, preflight: [...this.status.preflight] };
  }

  async check(): Promise<ServiceUpdateStatus> {
    if (this.operation) return this.operation;
    const operation = this.performCheck();
    this.operation = operation;
    try {
      return await operation;
    } finally {
      this.operation = null;
    }
  }

  async install(): Promise<ServiceUpdateStatus> {
    if (this.operation) throw new Error("已有更新任务正在执行，请稍后重试。");
    const operation = this.performInstall();
    this.operation = operation;
    try {
      return await operation;
    } finally {
      this.operation = null;
    }
  }

  private async performCheck(): Promise<ServiceUpdateStatus> {
    if (!this.enabled) {
      this.status = { ...this.status, phase: "idle", error: "服务端自动更新未启用。" };
      return this.getStatus();
    }
    this.status = { ...this.status, phase: "checking", error: null };
    try {
      const response = await this.fetchImpl(this.releaseApiUrl, {
        headers: {
          accept: "application/vnd.github+json",
          "user-agent": `sts2-lobby-service/${this.currentVersion}`,
          "x-github-api-version": "2022-11-28",
        },
        signal: AbortSignal.timeout(15_000),
      });
      if (!response.ok) throw new Error(`GitHub Release 请求失败（HTTP ${response.status}）。`);
      const payload = await response.json() as unknown;
      const release = Array.isArray(payload)
        ? selectLatestGithubServiceRelease(payload)
        : parseGithubServiceRelease(payload as GithubReleasePayload);
      this.release = release;
      const updateAvailable = compareServiceVersions(release.version, this.currentVersion) > 0;
      const preflight = await this.runPreflight();
      this.status = {
        ...this.status,
        latestVersion: release.version,
        updateAvailable,
        phase: updateAvailable ? "available" : "up_to_date",
        canUpdate: updateAvailable && preflight.length === 0,
        preflight,
        releaseUrl: release.releaseUrl || null,
        releaseNotes: release.notes || null,
        checkedAt: new Date().toISOString(),
        error: null,
      };
    } catch (error) {
      this.status = {
        ...this.status,
        phase: "failed",
        canUpdate: false,
        checkedAt: new Date().toISOString(),
        error: error instanceof Error ? error.message : "检查更新失败。",
      };
    }
    await this.persistStatus();
    return this.getStatus();
  }

  private async performInstall(): Promise<ServiceUpdateStatus> {
    if (!this.release || !this.status.updateAvailable) await this.performCheck();
    if (!this.release || !this.status.updateAvailable) {
      throw new Error(this.status.error ?? "当前没有可安装的新版本。");
    }
    const preflight = await this.runPreflight();
    if (preflight.length > 0) {
      this.status = { ...this.status, phase: "failed", canUpdate: false, preflight, error: preflight.join("；") };
      await this.persistStatus();
      throw new Error(this.status.error ?? "更新预检失败。");
    }

    const targetVersion = this.release.version;
    const workDir = join(this.dataDir, `staging-${targetVersion}-${Date.now()}`);
    const archivePath = join(workDir, ServiceAssetName);
    const extractDir = join(workDir, "extract");
    const currentDir = join(this.dataDir, "current");
    const rollbackDir = join(this.dataDir, "rollback");
    this.status = {
      ...this.status,
      phase: "downloading",
      canUpdate: false,
      startedAt: new Date().toISOString(),
      completedAt: null,
      error: null,
    };
    await this.persistStatus();

    try {
      await mkdir(extractDir, { recursive: true });
      const response = await this.fetchImpl(this.release.assetUrl, {
        headers: { accept: "application/octet-stream", "user-agent": `sts2-lobby-service/${this.currentVersion}` },
        signal: AbortSignal.timeout(120_000),
      });
      if (!response.ok) throw new Error(`服务更新包下载失败（HTTP ${response.status}）。`);
      const declaredSize = Number(response.headers.get("content-length") ?? "0");
      if (declaredSize > MaxArchiveBytes) throw new Error("服务更新包超过 128 MB 限制。");
      const bytes = Buffer.from(await response.arrayBuffer());
      if (bytes.length === 0 || bytes.length > MaxArchiveBytes) throw new Error("服务更新包大小无效。");
      await writeFile(archivePath, bytes, { mode: 0o600 });

      this.status = { ...this.status, phase: "installing" };
      await this.persistStatus();
      const { stdout } = await execFileAsync("unzip", ["-Z1", archivePath], { maxBuffer: 4 * 1024 * 1024 });
      validateServiceArchiveEntries(stdout.split(/\r?\n/).filter(Boolean));
      await execFileAsync("unzip", ["-q", archivePath, "-d", extractDir], { maxBuffer: 4 * 1024 * 1024 });

      const serviceDir = join(extractDir, "sts2_lobby_service", "lobby-service");
      const packageJson = JSON.parse(await readFile(join(serviceDir, "package.json"), "utf8")) as { version?: unknown };
      if (packageJson.version !== targetVersion) {
        throw new Error(`更新包版本 ${String(packageJson.version)} 与 Release ${targetVersion} 不一致。`);
      }
      const buildEnvironment = {
        ...process.env,
        NODE_ENV: "development",
        npm_config_production: "false",
      };
      await execFileAsync("npm", ["ci", "--include=dev"], {
        cwd: serviceDir,
        env: buildEnvironment,
        maxBuffer: 16 * 1024 * 1024,
      });
      await execFileAsync("npm", ["run", "build"], {
        cwd: serviceDir,
        env: buildEnvironment,
        maxBuffer: 16 * 1024 * 1024,
      });
      await execFileAsync("npm", ["prune", "--omit=dev"], {
        cwd: serviceDir,
        env: { ...process.env, NODE_ENV: "production", npm_config_production: "true" },
        maxBuffer: 16 * 1024 * 1024,
      });
      await access(join(serviceDir, "dist", "server.js"), fsConstants.R_OK);

      await rm(rollbackDir, { recursive: true, force: true });
      if (existsSync(currentDir)) await rename(currentDir, rollbackDir);
      await rename(serviceDir, currentDir);
      this.status = {
        ...this.status,
        phase: "restarting",
        completedAt: new Date().toISOString(),
        error: null,
      };
      await this.persistStatus();
      await rm(workDir, { recursive: true, force: true });
      setTimeout(this.restart, 750).unref();
      return this.getStatus();
    } catch (error) {
      await rm(workDir, { recursive: true, force: true }).catch(() => undefined);
      if (!existsSync(currentDir) && existsSync(rollbackDir)) {
        await rename(rollbackDir, currentDir).catch(() => undefined);
      }
      this.status = {
        ...this.status,
        phase: "failed",
        canUpdate: true,
        completedAt: new Date().toISOString(),
        error: error instanceof Error ? error.message : "安装更新失败。",
      };
      await this.persistStatus();
      throw error;
    }
  }

  private async runPreflight(): Promise<string[]> {
    const failures: string[] = [];
    if (process.platform !== "linux") failures.push("自动安装仅支持 Linux 服务端");
    if (this.deploymentMode === "unknown") failures.push("无法识别 Docker 或 systemd 部署方式");
    if (process.env.STS2_RUNTIME_LAUNCHER !== "1") failures.push("当前服务未通过持久化运行时启动器启动");
    try {
      await mkdir(this.dataDir, { recursive: true });
      await access(this.dataDir, fsConstants.R_OK | fsConstants.W_OK | fsConstants.X_OK);
    } catch {
      failures.push(`更新数据目录不可写：${this.dataDir}`);
    }
    if (this.deploymentMode === "docker") {
      const normalizedDataDir = `${this.dataDir}${sep}`;
      if (!normalizedDataDir.startsWith(`/app/data${sep}`)) failures.push("Docker 更新目录必须位于持久化的 /app/data 下");
    }
    for (const command of ["npm", "unzip"]) {
      try {
        await execFileAsync(command, [command === "npm" ? "--version" : "-v"], { timeout: 5000, maxBuffer: 1024 * 1024 });
      } catch {
        failures.push(`缺少更新所需命令：${command}`);
      }
    }
    return failures;
  }

  private async persistStatus(): Promise<void> {
    try {
      const statusPath = join(this.dataDir, "status.json");
      await mkdir(dirname(statusPath), { recursive: true });
      const temporaryPath = `${statusPath}.${process.pid}.tmp`;
      await writeFile(temporaryPath, `${JSON.stringify(this.status, null, 2)}\n`, { mode: 0o600 });
      await rename(temporaryPath, statusPath);
    } catch (error) {
      console.error("[service-update] failed to persist update status", error);
    }
  }
}
