import assert from "node:assert/strict";
import { spawnSync, type SpawnSyncReturns } from "node:child_process";
import { createHash } from "node:crypto";
import {
  lstatSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  readlinkSync,
  rmSync,
  statSync,
  symlinkSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const repositoryRoot = fileURLToPath(new URL("../../", import.meta.url));
const packageScript = join(repositoryRoot, "scripts", "package-lobby-service.sh");
const historicalRoots = [
  join(repositoryRoot, "releases"),
  join(repositoryRoot, "sts2-lan-connect", "release"),
  join(repositoryRoot, "lobby-service", "release"),
] as const;

const expectedFiles = [
  "LICENSE",
  "README.md",
  "THIRD_PARTY_NOTICES",
  "install-lobby-service-linux.sh",
  "lobby-service/.dockerignore",
  "lobby-service/.env.example",
  "lobby-service/Dockerfile",
  "lobby-service/deploy/.env.example",
  "lobby-service/deploy/docker-compose.lobby-service.yml",
  "lobby-service/deploy/lobby-service.docker.env.example",
  "lobby-service/deploy/sts2-lobby.service.example",
  "lobby-service/package-lock.json",
  "lobby-service/package.json",
  "lobby-service/scripts/generate-server-admin-password-hash.mjs",
  "lobby-service/src/app.ts",
  "lobby-service/src/bandwidth-guard.ts",
  "lobby-service/src/chat/dedupe-cache.ts",
  "lobby-service/src/chat/feature-resolver.ts",
  "lobby-service/src/chat/gateway.ts",
  "lobby-service/src/chat/history-buffer.ts",
  "lobby-service/src/chat/peer-registry.ts",
  "lobby-service/src/chat/protocol.ts",
  "lobby-service/src/chat/rate-limiter.ts",
  "lobby-service/src/chat/room-gateway.ts",
  "lobby-service/src/chat/ticket-store.ts",
  "lobby-service/src/chat/upgrade-router.ts",
  "lobby-service/src/client-ip.ts",
  "lobby-service/src/config.ts",
  "lobby-service/src/join-guard.ts",
  "lobby-service/src/mod-sync/diff.ts",
  "lobby-service/src/mod-sync/protocol.ts",
  "lobby-service/src/mod-sync/validator.ts",
  "lobby-service/src/peer/auto-announce.ts",
  "lobby-service/src/peer/bootstrap.ts",
  "lobby-service/src/peer/gossip.ts",
  "lobby-service/src/peer/handlers/announce.ts",
  "lobby-service/src/peer/handlers/health.ts",
  "lobby-service/src/peer/handlers/heartbeat.ts",
  "lobby-service/src/peer/handlers/list.ts",
  "lobby-service/src/peer/handlers/metrics.ts",
  "lobby-service/src/peer/identity.ts",
  "lobby-service/src/peer/prober.ts",
  "lobby-service/src/peer/seeds-loader.ts",
  "lobby-service/src/peer/store.ts",
  "lobby-service/src/peer/types.ts",
  "lobby-service/src/relay.ts",
  "lobby-service/src/rolling-bandwidth.ts",
  "lobby-service/src/room-cleanup.ts",
  "lobby-service/src/server-admin-auth.ts",
  "lobby-service/src/server-admin-state.ts",
  "lobby-service/src/server-admin-ui.ts",
  "lobby-service/src/server.ts",
  "lobby-service/src/store.ts",
  "lobby-service/tsconfig.json",
] as const;

function sha256(bytes: Buffer | string): string {
  return createHash("sha256").update(bytes).digest("hex");
}

function runPackage(
  args: readonly string[],
  cwd = repositoryRoot,
): SpawnSyncReturns<string> {
  return spawnSync(packageScript, args, {
    cwd,
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024,
  });
}

function requireSuccess(result: SpawnSyncReturns<string>): void {
  assert.equal(result.error, undefined);
  assert.equal(result.status, 0, result.stderr);
}

function listFiles(root: string, current = root): string[] {
  const files: string[] = [];
  for (const name of readdirSync(current).sort()) {
    const path = join(current, name);
    const stat = lstatSync(path);
    assert.equal(stat.isSymbolicLink(), false, `unexpected symlink: ${path}`);
    if (stat.isDirectory()) {
      files.push(...listFiles(root, path));
    } else if (stat.isFile()) {
      files.push(relative(root, path).split(sep).join("/"));
    }
  }
  return files.sort();
}

function zipFiles(zipPath: string, packageName: string): string[] {
  const result = spawnSync("zipinfo", ["-1", zipPath], { encoding: "utf8" });
  requireSuccess(result);
  const prefix = `${packageName}/`;
  const entries = result.stdout.split(/\r?\n/).filter(Boolean);
  assert.equal(new Set(entries).size, entries.length, "zip contains duplicate entries");
  for (const entry of entries) {
    assert.equal(entry.startsWith("/"), false, `absolute zip entry: ${entry}`);
    assert.equal(entry.includes("\\"), false, `backslash zip entry: ${entry}`);
    assert.equal(entry.split("/").includes(".."), false, `traversal zip entry: ${entry}`);
    assert.ok(entry === packageName || entry.startsWith(prefix), `escaped zip entry: ${entry}`);
  }
  return entries
    .filter((entry) => entry.startsWith(prefix) && !entry.endsWith("/"))
    .map((entry) => entry.slice(prefix.length))
    .sort();
}

function sourcePath(packagePath: string): string {
  if (packagePath === "LICENSE" || packagePath === "THIRD_PARTY_NOTICES") {
    return join(repositoryRoot, packagePath);
  }
  if (packagePath === "README.md") {
    return join(repositoryRoot, "lobby-service", "README.md");
  }
  if (packagePath === "install-lobby-service-linux.sh") {
    return join(repositoryRoot, "scripts", packagePath);
  }
  assert.ok(packagePath.startsWith("lobby-service/"));
  return join(repositoryRoot, ...packagePath.split("/"));
}

function snapshotTree(root: string): string {
  try {
    const stat = lstatSync(root);
    if (stat.isSymbolicLink()) return `L|${readlinkSync(root)}`;
    if (!stat.isDirectory()) return `F|${stat.mode}|${stat.size}|${sha256(readFileSync(root))}`;
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") return "MISSING";
    throw error;
  }

  const records = [`D|.|${statSync(root).mode}|${statSync(root).mtimeMs}`];
  const visit = (current: string): void => {
    for (const name of readdirSync(current).sort()) {
      const path = join(current, name);
      const stat = lstatSync(path);
      const item = relative(root, path).split(sep).join("/");
      if (stat.isSymbolicLink()) {
        records.push(`L|${item}|${readlinkSync(path)}`);
      } else if (stat.isDirectory()) {
        records.push(`D|${item}|${stat.mode}|${stat.mtimeMs}`);
        visit(path);
      } else {
        records.push(`F|${item}|${stat.mode}|${stat.size}|${stat.mtimeMs}|${sha256(readFileSync(path))}`);
      }
    }
  };
  visit(root);
  return sha256(records.join("\n"));
}

function snapshotHistorical(): string[] {
  return historicalRoots.map(snapshotTree);
}

function assertCleanManifest(files: readonly string[]): void {
  const forbiddenBinaries = new Set([
    "sts2.dll",
    "steamworks.net.dll",
    "0harmony.dll",
    "godotsharp.dll",
    "godotsharpeditor.dll",
  ]);
  for (const file of files) {
    const lower = file.toLowerCase();
    assert.equal(forbiddenBinaries.has(lower.split("/").at(-1)!), false, `forbidden binary: ${file}`);
    assert.doesNotMatch(lower, /(^|\/)typing\.dll$/);
    assert.doesNotMatch(lower, /(^|\/)\.env$/);
    assert.doesNotMatch(lower, /(^|\/)\.git(?:\/|$)/);
    assert.doesNotMatch(lower, /(^|\/)(?:test|tests)(?:\/|$)/);
    assert.doesNotMatch(lower, /(?:^|\.)test\.[cm]?[jt]s$/);
    assert.doesNotMatch(lower, /(?:secret|password|token)(?:\.|$)/);
    assert.doesNotMatch(lower, /(?:admin-state|server-admin\.json)$/);
    assert.doesNotMatch(lower, /\.(?:pck|png|jpe?g|ttf|otf)$/);
  }
}

test("service package uses exact production allowlist and deterministic temporary outputs", () => {
  const tempRoot = mkdtempSync(join(tmpdir(), "sts2 service package "));
  const before = snapshotHistorical();
  try {
    const relativeOutput = join("relative output", "with spaces");
    const first = runPackage(["--output-dir", relativeOutput], tempRoot);
    requireSuccess(first);

    const firstOutput = resolve(tempRoot, relativeOutput);
    const packageDir = join(firstOutput, "sts2_lobby_service");
    const firstZip = join(firstOutput, "sts2_lobby_service.zip");
    assert.deepEqual(listFiles(packageDir), [...expectedFiles].sort());
    assert.deepEqual(zipFiles(firstZip, "sts2_lobby_service"), [...expectedFiles].sort());
    assertCleanManifest(expectedFiles);

    const packageJson = JSON.parse(readFileSync(join(packageDir, "lobby-service/package.json"), "utf8")) as {
      version?: unknown;
    };
    const packageLock = JSON.parse(readFileSync(join(packageDir, "lobby-service/package-lock.json"), "utf8")) as {
      version?: unknown;
      packages?: Record<string, { version?: unknown }>;
    };
    assert.equal(packageJson.version, "0.5.1");
    assert.equal(packageLock.version, "0.5.1");
    assert.equal(packageLock.packages?.[""]?.version, "0.5.1");

    for (const packagePath of expectedFiles) {
      assert.deepEqual(
        readFileSync(join(packageDir, ...packagePath.split("/"))),
        readFileSync(sourcePath(packagePath)),
        `${packagePath} must use source-identical bytes`,
      );
    }

    const absoluteOutput = join(tempRoot, "absolute output [safe]");
    const second = runPackage(["--output-dir", absoluteOutput]);
    requireSuccess(second);
    assert.equal(
      sha256(readFileSync(firstZip)),
      sha256(readFileSync(join(absoluteOutput, "sts2_lobby_service.zip"))),
      "service zip must be byte-for-byte deterministic",
    );
    assert.deepEqual(snapshotHistorical(), before);
  } finally {
    rmSync(tempRoot, { recursive: true, force: true });
  }
});

test("service package rejects malformed protected traversal and symlink outputs", () => {
  const tempRoot = mkdtempSync(join(tmpdir(), "sts2 service package rejection "));
  const before = snapshotHistorical();
  try {
    for (const args of [
      ["--output-dir"],
      ["--output-dir", join(tempRoot, "one"), "--output-dir", join(tempRoot, "two")],
      ["--output-dir", join(repositoryRoot, "releases")],
      ["--output-dir", join(repositoryRoot, "releases", "nested")],
      ["--output-dir", join(repositoryRoot, "sts2-lan-connect", "release", "nested")],
      ["--output-dir", join(repositoryRoot, "lobby-service", "release", "nested")],
      ["--output-dir", join(repositoryRoot, "scripts", "package output")],
      ["--output-dir", `${tempRoot}/safe/../escape`],
    ]) {
      const result = runPackage(args);
      assert.notEqual(result.status, 0, `unexpected success for ${args.join(" ")}`);
    }

    const link = join(tempRoot, "release-link");
    symlinkSync(join(repositoryRoot, "releases"), link, "dir");
    const symlinkResult = runPackage(["--output-dir", join(link, "nested")]);
    assert.notEqual(symlinkResult.status, 0);
    assert.match(symlinkResult.stderr, /protected release output path/i);
    assert.deepEqual(snapshotHistorical(), before);
  } finally {
    rmSync(tempRoot, { recursive: true, force: true });
  }
});

test("release sources pin service v0.5.1 and client v0.5.2 while preserving older fixtures", () => {
  const servicePackage = JSON.parse(readFileSync(join(repositoryRoot, "lobby-service/package.json"), "utf8")) as {
    version?: unknown;
  };
  const serviceLock = JSON.parse(readFileSync(join(repositoryRoot, "lobby-service/package-lock.json"), "utf8")) as {
    version?: unknown;
    packages?: Record<string, { version?: unknown }>;
  };
  const clientManifest = JSON.parse(readFileSync(join(repositoryRoot, "sts2-lan-connect/sts2_lan_connect.json"), "utf8")) as {
    version?: unknown;
  };
  const clientProject = readFileSync(join(repositoryRoot, "sts2-lan-connect/sts2_lan_connect.csproj"), "utf8");
  assert.equal(servicePackage.version, "0.5.1");
  assert.equal(serviceLock.version, "0.5.1");
  assert.equal(serviceLock.packages?.[""]?.version, "0.5.1");
  assert.equal(clientManifest.version, "0.5.2");
  assert.match(clientProject, /<Version>0\.5\.2<\/Version>/);
  assert.match(clientProject, /<AssemblyVersion>0\.5\.2\.0<\/AssemblyVersion>/);

  const serviceFixture = readFileSync(
    join(repositoryRoot, "lobby-service/src/chat/compatibility.integration.test.ts"),
    "utf8",
  );
  const clientFixture = readFileSync(
    join(repositoryRoot, "sts2-lan-connect.Tests/Lobby/Chat/LanConnectRoomRichCompatibilityTests.cs"),
    "utf8",
  );
  for (const historicalVersion of ["0.5.0", "0.4.0", "0.2.2"]) {
    assert.match(`${serviceFixture}\n${clientFixture}`, new RegExp(`\\"${historicalVersion.replaceAll(".", "\\.")}\\"`));
  }
});
