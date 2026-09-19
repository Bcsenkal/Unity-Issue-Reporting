import assert from "node:assert/strict";
import { gzipSync } from "node:zlib";
import { test } from "node:test";
import { createHandler } from "./worker.mjs";

const env = {
  SLACK_BOT_TOKEN: "test-token",
  SLACK_CHANNEL_ID: "C123456",
  UPLOAD_ACCESS_CODE: "test-code",
  LOG_UPLOAD_LIMITER: { limit: async () => ({ success: true }) }
};

function report(details) {
  const headers = {
    Authorization: "Bearer test-code",
    "Content-Type": "application/gzip",
    "X-Log-Filename": "level.log.gz"
  };
  if (details !== undefined) headers["X-Report-Details"] = encodeURIComponent(details);
  return new Request("https://relay.example/logs", {
    method: "POST",
    headers,
    body: gzipSync("level log")
  });
}

test("issue details reach the Slack comment with mentions neutralized", async () => {
  let comment;
  const upstreamFetch = async (url, options) => {
    if (url.endsWith("files.getUploadURLExternal"))
      return Response.json({ ok: true, upload_url: "https://files.slack.com/upload", file_id: "F123" });
    if (url === "https://files.slack.com/upload") return new Response("ok");
    comment = JSON.parse(options.body).initial_comment;
    return Response.json({ ok: true });
  };
  const response = await createHandler(upstreamFetch)(report("Crash after revive @here <tag>"), env);
  assert.equal(response.status, 200);
  assert.match(comment, /Crash after revive _here _tag_/);
});

test("empty details are rejected before uploading", async () => {
  let called = false;
  const response = await createHandler(async () => { called = true; })(report("  "), env);
  assert.equal(response.status, 400);
  assert.equal(called, false);
});

test("older clients without a details header still upload", async () => {
  let comment;
  const upstreamFetch = async (url, options) => {
    if (url.endsWith("files.getUploadURLExternal"))
      return Response.json({ ok: true, upload_url: "https://files.slack.com/upload", file_id: "F123" });
    if (url === "https://files.slack.com/upload") return new Response("ok");
    comment = JSON.parse(options.body).initial_comment;
    return Response.json({ ok: true });
  };
  const response = await createHandler(upstreamFetch)(report(), env);
  assert.equal(response.status, 200);
  assert.match(comment, /older game build/);
});

test("another project can send a plain text attachment to the report endpoint", async () => {
  let comment;
  const upstreamFetch = async (url, options) => {
    if (url.endsWith("files.getUploadURLExternal"))
      return Response.json({ ok: true, upload_url: "https://files.slack.com/upload", file_id: "F123" });
    if (url === "https://files.slack.com/upload") return new Response("ok");
    comment = JSON.parse(options.body).initial_comment;
    return Response.json({ ok: true });
  };
  const request = new Request("https://relay.example/reports", {
    method: "POST",
    headers: {
      Authorization: "Bearer test-code",
      "Content-Type": "text/plain",
      "X-Report-Filename": "debug.txt",
      "X-Report-Context": "main menu",
      "X-Report-Details": "UI froze"
    },
    body: "plain log"
  });
  const response = await createHandler(upstreamFetch)(request, { ...env, PROJECT_NAME: "Future Game" });
  assert.equal(response.status, 200);
  assert.match(comment, /Future Game issue report \| main menu/);
  assert.match(comment, /UI froze/);
});
