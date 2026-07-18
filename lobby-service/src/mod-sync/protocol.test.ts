import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { resolveModDiff } from "./diff.js";
import {
  MOD_SYNC_MAX_DEPENDENCIES,
  MOD_SYNC_MAX_DESCRIPTORS,
  MOD_SYNC_MAX_ID_CHARACTERS,
  MOD_SYNC_MAX_VERSION_CHARACTERS,
  type LobbyModDescriptor,
  type ModSyncFixture,
} from "./protocol.js";
import {
  canonicalModInventoryJson,
  ModSyncValidationError,
  validateModInventory,
} from "./validator.js";

function descriptor(overrides: Partial<LobbyModDescriptor> = {}): LobbyModDescriptor {
  return {
    id: "fixture.gameplay",
    version: "1.0.0",
    role: "gameplay",
    source: "mods_directory",
    dependencies: [],
    ...overrides,
  };
}

function hasValidationCode(error: unknown): boolean {
  return error instanceof ModSyncValidationError && error.code === "invalid_mod_inventory";
}

test("validator canonicalizes trimmed ordinal-sorted descriptors and dependencies", () => {
  assert.deepEqual(
    validateModInventory([
      descriptor({ id: " z.mod ", dependencies: [" dep.b ", "dep.a", "dep.a"] }),
      descriptor({ id: "A.mod" }),
    ]),
    [
      descriptor({ id: "A.mod" }),
      descriptor({ id: "z.mod", dependencies: ["dep.a", "dep.b"] }),
    ],
  );
});

test("validator accepts exact descriptor dependency id version and workshop limits", () => {
  const inventory = Array.from({ length: MOD_SYNC_MAX_DESCRIPTORS }, (_, index) => descriptor({
    id: `${String(index).padStart(2, "0")}.${"x".repeat(MOD_SYNC_MAX_ID_CHARACTERS - 3)}`,
    version: "v".repeat(MOD_SYNC_MAX_VERSION_CHARACTERS),
    source: "steam_workshop",
    workshopFileId: "9".repeat(20),
    dependencies: Array.from({ length: MOD_SYNC_MAX_DEPENDENCIES }, (__, dep) => `dep.${index}.${dep}`),
  }));

  assert.equal(validateModInventory(inventory, { maxPayloadBytes: 1_000_000 }).length, 64);
});

test("validator rejects non-array 65 descriptors and unknown fields", () => {
  assert.throws(() => validateModInventory({}), hasValidationCode);
  assert.throws(
    () => validateModInventory(Array.from({ length: 65 }, (_, index) => descriptor({ id: `mod.${index}` }))),
    hasValidationCode,
  );
  assert.throws(
    () => validateModInventory([{ ...descriptor(), downloadUrl: "https://invalid.example/mod.zip" }]),
    hasValidationCode,
  );
});

test("validator rejects invalid roles sources and missing required fields", () => {
  assert.throws(() => validateModInventory([descriptor({ role: "invalid" as "gameplay" })]), hasValidationCode);
  assert.throws(() => validateModInventory([descriptor({ source: "url" as "unknown" })]), hasValidationCode);
  const missing = { ...descriptor() } as Record<string, unknown>;
  delete missing.dependencies;
  assert.throws(() => validateModInventory([missing]), hasValidationCode);
});

test("validator rejects empty duplicate control and reserved ids", () => {
  for (const id of ["", "bad\u0001id", "bad\u0085id", "sts2_lan_connect", "lan_connect.private", "sts2-lan-connect.private"]) {
    assert.throws(() => validateModInventory([descriptor({ id })]), hasValidationCode);
  }
  assert.throws(() => validateModInventory([descriptor({ id: "same" }), descriptor({ id: " same " })]), hasValidationCode);
});

test("validator rejects id version dependency and payload overflow", () => {
  assert.throws(() => validateModInventory([descriptor({ id: "x".repeat(129) })]), hasValidationCode);
  assert.throws(() => validateModInventory([descriptor({ version: "x".repeat(65) })]), hasValidationCode);
  assert.throws(
    () => validateModInventory([descriptor({ dependencies: Array.from({ length: 17 }, (_, index) => `dep.${index}`) })]),
    hasValidationCode,
  );
  assert.throws(() => validateModInventory([descriptor({ dependencies: ["x".repeat(129)] })]), hasValidationCode);
  assert.throws(() => validateModInventory([descriptor()], { maxPayloadBytes: 16 }), hasValidationCode);
});

test("validator rejects invalid workshop ids and preserves decimal strings", () => {
  for (const workshopFileId of [
    "",
    "0",
    "00",
    "-1",
    "+1",
    " 1",
    "1 ",
    "１",
    "1.0",
    "abc",
    "1".repeat(21),
  ]) {
    assert.throws(
      () => validateModInventory([descriptor({ source: "steam_workshop", workshopFileId })]),
      hasValidationCode,
    );
  }
  assert.equal(
    validateModInventory([descriptor({ source: "steam_workshop", workshopFileId: "18446744073709551615" })])[0]?.workshopFileId,
    "18446744073709551615",
  );
  assert.throws(
    () => validateModInventory([descriptor({ source: "steam_workshop", workshopFileId: 3747497501 as unknown as string })]),
    hasValidationCode,
  );
});

test("validator fuzz rejects untrusted payload shapes with controlled errors", () => {
  const malformed: unknown[] = [
    null,
    undefined,
    true,
    1,
    "[]",
    {},
    [null],
    [new Date()],
    [descriptor({ id: "bad\u0000id" })],
    [descriptor({ version: "bad\u0085version" })],
    [descriptor({ role: "GAMEPLAY" as "gameplay" })],
    [descriptor({ source: "https://invalid.example/mod.zip" as "unknown" })],
    [descriptor({ workshopFileId: "1e3", source: "steam_workshop" })],
    [{ ...descriptor(), dependencies: { length: 0 } }],
    [{ ...descriptor(), nested: { downloadUrl: "https://invalid.example/private.zip" } }],
    Array.from({ length: 65 }, (_, index) => descriptor({ id: `oversize.${index}` })),
    [descriptor({ dependencies: Array.from({ length: 17 }, (_, index) => `dep.${index}`) })],
  ];
  const nullPrototype = Object.create(null) as Record<string, unknown>;
  Object.assign(nullPrototype, descriptor({ id: "null.prototype" }), { downloadUrl: "file:///tmp/mod.dll" });
  malformed.push([nullPrototype]);

  for (let round = 0; round < 32; round += 1) {
    for (const payload of malformed) {
      assert.throws(() => validateModInventory(payload), hasValidationCode);
    }
  }
});

test("canonical JSON uses cross-runtime property order and UTF-8 text", () => {
  assert.equal(
    canonicalModInventoryJson([descriptor({ id: "mod.测试", version: "版本一" })]),
    "[{\"id\":\"mod.测试\",\"version\":\"版本一\",\"role\":\"gameplay\",\"source\":\"mods_directory\",\"dependencies\":[]}]",
  );
});

test("diff matches the shared canonical fixture", () => {
  const fixture = JSON.parse(readFileSync(
    new URL("../../../test-fixtures/mod-sync/canonical-diff-v1.json", import.meta.url),
    "utf8",
  )) as ModSyncFixture;

  const result = resolveModDiff({
    hostGameVersion: fixture.gameVersion.host,
    localGameVersion: fixture.gameVersion.local,
    hostMods: fixture.hostMods,
    localMods: fixture.localMods,
  });

  assert.equal(result.gameVersion.exactMatch, true);
  assert.deepEqual(result.missingWorkshopMods.map((mod) => mod.id), fixture.expected.missingWorkshopModIds);
  assert.deepEqual(result.missingManualMods.map((mod) => mod.id), fixture.expected.missingManualModIds);
  assert.deepEqual(result.extraGameplayMods.map((mod) => mod.id), fixture.expected.extraGameplayModIds);
  assert.deepEqual(result.versionMismatches.map((mod) => mod.id), fixture.expected.versionMismatchModIds);
  assert.equal(result.canContinueRelaxed, fixture.expected.canContinueRelaxed);
});

test("diff excludes dependency-only local extras", () => {
  const result = resolveModDiff({
    hostGameVersion: "v0.109.0",
    localGameVersion: "v0.109.0",
    hostMods: [],
    localMods: [descriptor({ id: "extra.dependency", role: "dependency" })],
  });
  assert.deepEqual(result.extraGameplayMods, []);
});

test("diff blocks cross-game versions before exposing mod differences", () => {
  const result = resolveModDiff({
    hostGameVersion: "v0.108.0",
    localGameVersion: "v0.109.0",
    hostMods: [descriptor({ id: "host.only" })],
    localMods: [descriptor({ id: "local.only" })],
  });

  assert.equal(result.gameVersion.exactMatch, false);
  assert.equal(result.canContinueRelaxed, false);
  assert.deepEqual(result.missingWorkshopMods, []);
  assert.deepEqual(result.missingManualMods, []);
  assert.deepEqual(result.extraGameplayMods, []);
  assert.deepEqual(result.versionMismatches, []);
});

test("diff uses exact mod versions and treats workshop entries without ids as manual", () => {
  const result = resolveModDiff({
    hostGameVersion: "v0.109.0",
    localGameVersion: "v0.109.0",
    hostMods: [
      descriptor({ id: "versioned", version: "1.0.0", source: "steam_workshop", workshopFileId: "123" }),
      descriptor({ id: "no.workshop.id", source: "steam_workshop" }),
    ],
    localMods: [descriptor({ id: "versioned", version: "1.0" })],
  });

  assert.deepEqual(result.versionMismatches.map((mod) => mod.id), ["versioned"]);
  assert.deepEqual(result.missingManualMods.map((mod) => mod.id), ["no.workshop.id"]);
});
