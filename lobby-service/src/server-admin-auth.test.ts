import assert from "node:assert/strict";
import test from "node:test";
import {
  createServerAdminCsrfToken,
  digestServerAdminCsrfToken,
  verifyServerAdminCsrfToken,
} from "./server-admin-auth.js";

test("server admin CSRF tokens are random 32-byte base64url values", () => {
  const tokens = new Set(Array.from({ length: 32 }, () => createServerAdminCsrfToken()));
  assert.equal(tokens.size, 32);
  for (const token of tokens) {
    assert.match(token, /^[A-Za-z0-9_-]{43}$/);
    assert.equal(Buffer.from(token, "base64url").length, 32);
  }
});

test("server admin CSRF digests are stable and never equal the raw token", () => {
  const token = createServerAdminCsrfToken();
  const first = digestServerAdminCsrfToken(token);
  const second = digestServerAdminCsrfToken(token);

  assert.equal(first.length, 32);
  assert.deepEqual(first, second);
  assert.notEqual(first.toString("base64url"), token);
});

test("server admin CSRF verification accepts only the matching token", () => {
  const token = createServerAdminCsrfToken();
  const other = createServerAdminCsrfToken();
  const digest = digestServerAdminCsrfToken(token);

  assert.equal(verifyServerAdminCsrfToken(token, digest), true);
  assert.equal(verifyServerAdminCsrfToken(other, digest), false);
  assert.equal(verifyServerAdminCsrfToken(undefined, digest), false);
  assert.equal(verifyServerAdminCsrfToken("", digest), false);
  assert.equal(verifyServerAdminCsrfToken("x", digest), false);
  assert.equal(verifyServerAdminCsrfToken("x".repeat(8_192), digest), false);
});

test("server admin CSRF digests remain session-specific across token rotation", () => {
  const oldToken = createServerAdminCsrfToken();
  const nextToken = createServerAdminCsrfToken();
  const oldDigest = digestServerAdminCsrfToken(oldToken);
  const nextDigest = digestServerAdminCsrfToken(nextToken);

  assert.equal(verifyServerAdminCsrfToken(oldToken, nextDigest), false);
  assert.equal(verifyServerAdminCsrfToken(nextToken, oldDigest), false);
  assert.equal(verifyServerAdminCsrfToken(nextToken, nextDigest), true);
});
