import test from "node:test";
import assert from "node:assert/strict";
import {
  ipMatchesCidr,
  normalizeIp,
  parseTrustedProxyCidrs,
  resolveClientIp,
  type ClientIpRequest,
} from "./client-ip.js";

function req(remoteAddress: string | undefined, headers: Record<string, string> = {}): ClientIpRequest {
  const normalizedHeaders = Object.fromEntries(
    Object.entries(headers).map(([name, value]) => [name.toLowerCase(), value]),
  );

  return {
    socket: { remoteAddress },
    headers: normalizedHeaders,
  };
}

test("parseTrustedProxyCidrs trims comma-separated CIDRs", () => {
  assert.deepEqual(
    parseTrustedProxyCidrs(" 10.0.0.0/8, 192.168.0.0/16 , 2001:db8::/32 "),
    ["10.0.0.0/8", "192.168.0.0/16", "2001:db8::/32"],
  );
  assert.deepEqual(parseTrustedProxyCidrs(undefined), []);
});

test("normalizeIp collapses IPv4-mapped IPv6 addresses and rejects hostnames", () => {
  assert.equal(normalizeIp(" ::ffff:127.0.0.1 "), "127.0.0.1");
  assert.equal(normalizeIp("::ffff:c000:201"), "192.0.2.1");
  assert.equal(ipMatchesCidr("::ffff:c000:201", "192.0.2.0/24"), true);
  assert.equal(resolveClientIp(req("::ffff:c000:201"), []), "192.0.2.1");
  assert.equal(normalizeIp("2001:DB8::10"), "2001:db8::10");
  assert.equal(normalizeIp("example.com"), "");
  assert.equal(normalizeIp(""), "");
});

test("normalizeIp recognizes IPv6 addresses with embedded IPv4 tails", () => {
  assert.equal(normalizeIp("2001:db8::192.0.2.1"), "2001:db8::192.0.2.1");
});

test("normalizeIp rejects IPv6 addresses with a leading or trailing single colon", () => {
  assert.equal(normalizeIp(":1:2:3:4:5:6:7:8"), "");
  assert.equal(normalizeIp("1:2:3:4:5:6:7:8:"), "");
});

test("ipMatchesCidr supports IPv4 and IPv6 CIDRs", () => {
  assert.equal(ipMatchesCidr("127.0.0.1", "127.0.0.1"), true);
  assert.equal(ipMatchesCidr("::ffff:127.0.0.1", "127.0.0.1"), true);
  assert.equal(ipMatchesCidr("127.0.0.42", "127.0.0.0/24"), true);
  assert.equal(ipMatchesCidr("127.0.1.42", "127.0.0.0/24"), false);
  assert.equal(ipMatchesCidr("2001:db8::10", "2001:db8::/64"), true);
  assert.equal(ipMatchesCidr("2001:db9::10", "2001:db8::/64"), false);
});

test("ipMatchesCidr preserves IPv4-mapped IPv6 CIDR prefix semantics", () => {
  assert.equal(ipMatchesCidr("::ffff:192.0.2.1", "::ffff:192.0.2.0/120"), true);
});

test("untrusted peer cannot spoof forwarding headers", () => {
  assert.equal(resolveClientIp(req("203.0.113.8", { "x-forwarded-for": "198.51.100.7" }), []), "203.0.113.8");
});

test("trusted proxy resolves the first Forwarded hop", () => {
  assert.equal(resolveClientIp(req("10.0.0.4", { forwarded: "for=198.51.100.7;proto=https" }), ["10.0.0.0/8"]), "198.51.100.7");
});

test("trusted mapped IPv6 proxy resolves Forwarded hops", () => {
  assert.equal(
    resolveClientIp(req("::ffff:192.0.2.1", {
      forwarded: "for=198.51.100.7",
      "x-forwarded-for": "203.0.113.8",
    }), ["::ffff:192.0.2.0/120"]),
    "198.51.100.7",
  );
});

test("trusted mapped IPv6 proxy resolves X-Forwarded-For hops", () => {
  assert.equal(
    resolveClientIp(req("::ffff:192.0.2.1", { "x-forwarded-for": "203.0.113.8" }), ["::ffff:192.0.2.0/120"]),
    "203.0.113.8",
  );
});

test("trusted proxy resolves quoted and bracketed Forwarded IPv6 hops", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", { forwarded: "for=\"[2001:DB8::7]:443\";proto=https" }), ["10.0.0.0/8"]),
    "2001:db8::7",
  );
});

test("trusted proxy resolves bracketed Forwarded IPv6 hops with embedded IPv4 tails", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", { forwarded: "for=\"[2001:db8::192.0.2.1]\"" }), ["10.0.0.0/8"]),
    "2001:db8::192.0.2.1",
  );
});

test("trusted proxy falls back to X-Forwarded-For when Forwarded is malformed", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=example.com;proto=https, for=198.51.100.9",
      "x-forwarded-for": "198.51.100.7, 10.0.0.4",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects an unterminated Forwarded quote before falling back to X-Forwarded-For", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=198.51.100.8;proto=\"unterminated",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects unquoted and malformed Forwarded IPv6 values", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=2001:db8::8",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=\"[:1:2:3:4:5:6:7:8]\"",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects a Forwarded IPv6 port outside the valid range", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=\"[2001:db8::9]:65536\"",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects malformed Forwarded quoted strings and parameter whitespace", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=203.0.113.9;proto=\"a\"b\"c\"",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for = 203.0.113.9",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects whitespace before a Forwarded parameter separator", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=203.0.113.8 ;proto=https",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects whitespace after a Forwarded parameter separator", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=203.0.113.8; proto=https",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects whitespace inside a bracketed Forwarded IPv6 node", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=\"[ 2001:db8::8 ]\"",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects whitespace inside a quoted Forwarded node", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=\" 203.0.113.8 \"",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects bracketed Forwarded IPv4 nodes", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", { forwarded: "for=\"[192.0.2.99]\"" }), ["10.0.0.0/8"]),
    "10.0.0.4",
  );
});

test("trusted proxy accepts bracketed IPv4-mapped IPv6 nodes", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", { forwarded: "for=\"[::ffff:192.0.2.99]\"" }), ["10.0.0.0/8"]),
    "192.0.2.99",
  );
});

test("trusted proxy rejects a Forwarded IPv6 port longer than five digits", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=\"[2001:db8::8]:999999\"",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects duplicate Forwarded parameter names case-insensitively", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=198.51.100.8;For=203.0.113.9",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects a Forwarded chain containing a hostname", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", {
      forwarded: "for=198.51.100.8, for=example.com",
      "x-forwarded-for": "198.51.100.7",
    }), ["10.0.0.0/8"]),
    "198.51.100.7",
  );
});

test("trusted proxy rejects X-Forwarded-For with an empty first candidate", () => {
  assert.equal(
    resolveClientIp(req("10.0.0.4", { "x-forwarded-for": ", 198.51.100.7" }), ["10.0.0.0/8"]),
    "10.0.0.4",
  );
});

test("trusted proxy keeps the direct peer for empty or hostname forwarding values", () => {
  assert.equal(resolveClientIp(req("10.0.0.4", { forwarded: "for=example.com" }), ["10.0.0.0/8"]), "10.0.0.4");
  assert.equal(resolveClientIp(req("10.0.0.4", { "x-forwarded-for": "" }), ["10.0.0.0/8"]), "10.0.0.4");
});
