#!/usr/bin/env node
import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const bundledRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const updateDataDir = resolve(process.env.SERVER_UPDATE_DATA_DIR || join(bundledRoot, "data", "service-update"));
const currentRoot = join(updateDataDir, "current");

function readVersion(root) {
  try {
    const parsed = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));
    return typeof parsed.version === "string" ? parsed.version : null;
  } catch {
    return null;
  }
}

function versionParts(version) {
  const match = /^(?:v)?(\d+)\.(\d+)\.(\d+)$/.exec(version || "");
  return match ? match.slice(1).map(Number) : null;
}

function isAtLeast(candidate, bundled) {
  const left = versionParts(candidate);
  const right = versionParts(bundled);
  if (!left || !right) return false;
  for (let index = 0; index < 3; index += 1) {
    if (left[index] !== right[index]) return left[index] > right[index];
  }
  return true;
}

const bundledVersion = readVersion(bundledRoot);
const currentVersion = readVersion(currentRoot);
const currentEntry = join(currentRoot, "dist", "server.js");
const selectedRoot = currentVersion && bundledVersion && isAtLeast(currentVersion, bundledVersion) && existsSync(currentEntry)
  ? currentRoot
  : bundledRoot;
const entry = join(selectedRoot, "dist", "server.js");

console.log(`[service-runtime] bundled=${bundledVersion || "unknown"} selected=${readVersion(selectedRoot) || "unknown"} mode=${selectedRoot === bundledRoot ? "bundled" : "updated"}`);

const child = spawn(process.execPath, ["--enable-source-maps", entry], {
  cwd: bundledRoot,
  env: {
    ...process.env,
    STS2_RUNTIME_LAUNCHER: "1",
    SERVER_UPDATE_DATA_DIR: updateDataDir,
  },
  stdio: "inherit",
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    if (!child.killed) child.kill(signal);
  });
}

child.once("error", (error) => {
  console.error("[service-runtime] failed to launch service", error);
  process.exit(1);
});
child.once("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }
  process.exit(code ?? 1);
});
