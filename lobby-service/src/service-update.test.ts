import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import {
  compareServiceVersions,
  detectServiceDeploymentMode,
  normalizeServiceVersion,
  parseGithubServiceRelease,
  ServiceUpdateManager,
  selectLatestGithubServiceRelease,
  validateServiceArchiveEntries,
} from "./service-update.js";

test("service versions normalize v-prefix and compare numerically", () => {
  assert.equal(normalizeServiceVersion("v0.5.2"), "0.5.2");
  assert.equal(normalizeServiceVersion("0.5.10"), "0.5.10");
  assert.equal(normalizeServiceVersion("0.5"), null);
  assert.equal(compareServiceVersions("0.5.10", "0.5.2"), 8);
  assert.equal(compareServiceVersions("v0.5.2", "0.5.2"), 0);
});

test("GitHub release parser selects the exact service asset", () => {
  assert.deepEqual(parseGithubServiceRelease({
    tag_name: "v0.5.2",
    html_url: "https://github.com/example/release",
    body: "notes",
    draft: false,
    prerelease: false,
    assets: [
      { name: "sts2_lan_connect-release.zip", browser_download_url: "https://example/client.zip" },
      { name: "sts2_lobby_service.zip", browser_download_url: "https://example/service.zip" },
    ],
  }), {
    version: "0.5.2",
    assetUrl: "https://example/service.zip",
    releaseUrl: "https://github.com/example/release",
    notes: "notes",
  });
  assert.throws(() => parseGithubServiceRelease({ tag_name: "v0.5.2", assets: [] }), /缺少/);
});

test("release list selects the newest stable release that contains a service build", () => {
  assert.deepEqual(selectLatestGithubServiceRelease([
    {
      tag_name: "v0.5.2",
      draft: false,
      prerelease: false,
      assets: [{ name: "sts2_lan_connect-release.zip", browser_download_url: "https://example/client.zip" }],
    },
    {
      tag_name: "v0.5.2-rc.1",
      draft: false,
      prerelease: true,
      assets: [{ name: "sts2_lobby_service.zip", browser_download_url: "https://example/rc.zip" }],
    },
    {
      tag_name: "v0.5.1",
      html_url: "https://example/service-release",
      draft: false,
      prerelease: false,
      assets: [{ name: "sts2_lobby_service.zip", browser_download_url: "https://example/service.zip" }],
    },
  ]), {
    version: "0.5.1",
    assetUrl: "https://example/service.zip",
    releaseUrl: "https://example/service-release",
    notes: "",
  });
});

test("service archive validation rejects traversal and foreign roots", () => {
  validateServiceArchiveEntries([
    "sts2_lobby_service/",
    "sts2_lobby_service/lobby-service/package.json",
    "sts2_lobby_service/lobby-service/src/server.ts",
  ]);
  assert.throws(() => validateServiceArchiveEntries([
    "sts2_lobby_service/lobby-service/package.json",
    "sts2_lobby_service/../outside",
  ]), /超出/);
  assert.throws(() => validateServiceArchiveEntries([
    "other/lobby-service/package.json",
  ]), /超出/);
});

test("deployment mode honors explicit safe modes", () => {
  assert.equal(detectServiceDeploymentMode({ SERVER_UPDATE_DEPLOYMENT_MODE: "docker" }), "docker");
  assert.equal(detectServiceDeploymentMode({ SERVER_UPDATE_DEPLOYMENT_MODE: "systemd" }), "systemd");
  assert.equal(detectServiceDeploymentMode({ INVOCATION_ID: "abc" }), "systemd");
});

test("update manager reports a newer stable release and deployment preflight", async () => {
  const dataDir = mkdtempSync(join(tmpdir(), "sts2-service-update-"));
  try {
    const manager = new ServiceUpdateManager({
      currentVersion: "0.5.1",
      enabled: true,
      dataDir,
      deploymentMode: "systemd",
      fetchImpl: async () => new Response(JSON.stringify([{
        tag_name: "v0.5.2",
        html_url: "https://example/release",
        body: "release notes",
        draft: false,
        prerelease: false,
        assets: [{
          name: "sts2_lobby_service.zip",
          browser_download_url: "https://example/service.zip",
        }],
      }]), { status: 200, headers: { "content-type": "application/json" } }),
    });
    const status = await manager.check();
    assert.equal(status.currentVersion, "0.5.1");
    assert.equal(status.latestVersion, "0.5.2");
    assert.equal(status.updateAvailable, true);
    assert.equal(status.phase, "available");
    assert.equal(status.deploymentMode, "systemd");
    assert.ok(status.preflight.some((failure) => failure.includes("持久化运行时启动器")));
    assert.equal(status.canUpdate, false);
  } finally {
    rmSync(dataDir, { recursive: true, force: true });
  }
});
