#!/usr/bin/env node
import { randomBytes, scryptSync } from "node:crypto";

const password = process.argv[2];

if (typeof password !== "string" || password.trim() === "") {
  console.error("Usage: npm run hash-admin-password -- '<password>'");
  process.exit(1);
}

const salt = randomBytes(16).toString("hex");
const hash = scryptSync(password, salt, 64).toString("hex");
process.stdout.write(`${salt}:${hash}\n`);
