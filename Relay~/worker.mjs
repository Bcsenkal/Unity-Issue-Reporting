const SLACK_API = "https://slack.com/api/";
const DEFAULT_MAX_BYTES = 10 * 1024 * 1024;
const UPSTREAM_TIMEOUT_MS = 12000;
const FILE_TYPES = new Map([
  ["application/gzip", /\.(?:log\.)?gz$/],
  ["text/plain", /\.(?:log|txt)$/],
  ["application/json", /\.json$/],
  ["application/zip", /\.zip$/]
]);
const SAFE_FAILURE_CODES = new Set([
  "invalid_auth", "not_authed", "token_expired", "token_revoked", "account_inactive", "missing_scope",
  "not_allowed_token_type", "no_permission", "not_in_channel", "channel_not_found", "invalid_channel",
  "invalid_arguments", "missing_argument", "invalid_post_type", "invalid_form_data", "invalid_json", "json_not_object", "file_not_found",
  "file_type_not_allowed", "file_upload_size_restricted", "file_uploads_disabled", "file_uploads_except_images_disabled",
  "storage_limit_reached", "ratelimited", "request_timeout", "service_unavailable", "internal_error",
  "slack_http_error", "slack_api_error", "slack_upload_error", "invalid_upload_target"
]);

const reply = (status, ok, error, details = {}) => Response.json({ ok, ...(error ? { error } : {}), ...details }, {
  status, headers: { "Cache-Control": "no-store" }
});

async function readBounded(body, limit) {
  if (!body) return new Uint8Array();
  const reader = body.getReader();
  const chunks = [];
  let length = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      length += value.length;
      if (length > limit) { await reader.cancel(); return null; }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return bytes;
}

function metadata(request, name, fallback) {
  const raw = request.headers.get(name);
  if (!raw) return fallback;
  if (raw.length > 768) throw new Error("metadata_too_long");
  return decodeURIComponent(raw).slice(0, 160).replace(/[\r\n<>@&]/g, "_");
}

function reportDetails(request) {
  const raw = request.headers.get("X-Report-Details");
  if (raw === null) return "No description provided (older game build).";
  if (raw.length > 6000) throw new Error("details_too_long");
  const details = decodeURIComponent(raw).trim();
  if (!details || details.length > 500) throw new Error("invalid_details");
  return details.replace(/[\r\n\t]+/g, " ").replace(/[<>@&]/g, "_");
}

async function equalCode(actual, expected) {
  const encoder = new TextEncoder();
  const [a, b] = await Promise.all([actual, expected].map(value =>
    crypto.subtle.digest("SHA-256", encoder.encode(value))));
  const left = new Uint8Array(a), right = new Uint8Array(b);
  let difference = 0;
  for (let index = 0; index < left.length; index++) difference |= left[index] ^ right[index];
  return difference === 0;
}

/** Receives one bounded report file and shares it only to the server-configured Slack channel. */
export function createHandler(upstreamFetch = fetch) {
  return async function handle(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/health" && request.method === "GET") return reply(200, true);
    const diagnostic = url.pathname === "/diagnose";
    if (url.pathname !== "/logs" && url.pathname !== "/reports" && !diagnostic)
      return reply(404, false, "not_found");
    if (request.method !== "POST") return reply(405, false, "post_required");
    if (!env.SLACK_BOT_TOKEN || !/^[CG][A-Z0-9]+$/.test(env.SLACK_CHANNEL_ID ?? "")
        || !env.UPLOAD_ACCESS_CODE || !env.LOG_UPLOAD_LIMITER) return reply(503, false, "relay_not_configured");
    const authorization = request.headers.get("Authorization") ?? "";
    if (authorization.length > 512 || !await equalCode(authorization, "Bearer " + env.UPLOAD_ACCESS_CODE))
      return reply(401, false, "invalid_upload_code");
    if (!(await env.LOG_UPLOAD_LIMITER.limit({ key: "issue-reports" })).success)
      return reply(429, false, "rate_limited");
    const contentType = request.headers.get("Content-Type")?.split(";")[0];
    if (!diagnostic && !FILE_TYPES.has(contentType)) return reply(415, false, "unsupported_file_type");
    const configuredLimit = Number(env.MAX_UPLOAD_BYTES ?? DEFAULT_MAX_BYTES);
    const maxBytes = Number.isSafeInteger(configuredLimit) && configuredLimit > 0 ? configuredLimit : DEFAULT_MAX_BYTES;
    if (Number(request.headers.get("Content-Length")) > maxBytes) return reply(413, false, "log_too_large");

    let filename, level, version, platform, details, bytes;
    if (diagnostic) {
      // Exercise Slack's upload service with fixed synthetic bytes, then discard the unshared upload.
      filename = "issue-report-relay-diagnostic.txt";
      bytes = new TextEncoder().encode("Issue report relay diagnostic. No player data.\n");
    } else {
      try {
        filename = metadata(request, "X-Report-Filename", "")
          || metadata(request, "X-Log-Filename", "report.log.gz");
        level = metadata(request, "X-Report-Context", "")
          || metadata(request, "X-Log-Level", "report");
        version = metadata(request, "X-Game-Version", "unknown");
        platform = metadata(request, "X-Game-Platform", "unknown");
        details = reportDetails(request);
        if (!/^[A-Za-z0-9_. -]+$/.test(filename) || !FILE_TYPES.get(contentType).test(filename))
          return reply(400, false, "invalid_filename");
        bytes = await readBounded(request.body, maxBytes);
      } catch { return reply(400, false, "invalid_log_request"); }
      if (bytes === null) return reply(413, false, "log_too_large");
      if (bytes.length === 0) return reply(400, false, "empty_file");
      if (contentType === "application/gzip" && (bytes.length < 18 || bytes[0] !== 0x1f || bytes[1] !== 0x8b))
        return reply(400, false, "invalid_gzip");
    }

    let stage = "upload_url";
    async function slack(method, body) {
      const form = method === "files.getUploadURLExternal";
      const response = await upstreamFetch(SLACK_API + method, {
        method: "POST", redirect: "manual", signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MS),
        headers: { Authorization: "Bearer " + env.SLACK_BOT_TOKEN,
          "Content-Type": form ? "application/x-www-form-urlencoded; charset=utf-8" : "application/json; charset=utf-8" },
        body: form ? new URLSearchParams(body).toString() : JSON.stringify(body)
      });
      if (!response.ok) throw Object.assign(new Error("slack_http_error"), { code: "slack_http_error", status: response.status });
      const result = await response.json();
      if (result.ok !== true) throw Object.assign(new Error("slack_api_error"), { code: result.error });
      return result;
    }
    try {
      const slot = await slack("files.getUploadURLExternal", { filename, length: bytes.length });
      const uploadUrl = new URL(slot.upload_url);
      if (uploadUrl.protocol !== "https:" || uploadUrl.hostname !== "files.slack.com"
          || uploadUrl.username || uploadUrl.password || !slot.file_id) throw Object.assign(new Error("invalid_upload_target"), { code: "invalid_upload_target" });
      stage = "upload_bytes";
      const upload = await upstreamFetch(uploadUrl.href, {
        method: "POST", body: bytes, redirect: "manual", signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MS),
        headers: { "Content-Type": "application/octet-stream" }
      });
      if (!upload.ok) throw Object.assign(new Error("slack_upload_error"), { code: "slack_upload_error", status: upload.status });
      await upload.body?.cancel();
      if (diagnostic) return Response.json({ diagnostic_ok: true, shared: false }, { headers: { "Cache-Control": "no-store" } });
      stage = "complete_upload";
      await slack("files.completeUploadExternal", {
        files: [{ id: slot.file_id, title: filename }], channel_id: env.SLACK_CHANNEL_ID,
        initial_comment: `${String(env.PROJECT_NAME ?? "Game").slice(0, 160).replace(/[\r\n<>@&]/g, "_")} issue report | ${level} | ${platform} | version ${version}\n${details}`
      });
      return reply(200, true);
    } catch (failure) {
      // Only fixed stage names, allowlisted codes and HTTP status numbers can leave the relay.
      const code = SAFE_FAILURE_CODES.has(failure.code) ? failure.code
        : ["TimeoutError", "AbortError"].includes(failure.name) ? "request_timeout"
        : failure.name === "SyntaxError" ? "invalid_response"
        : failure.message === "Illegal invocation" ? "fetch_context_error"
        : failure.name === "TypeError" ? "network_error" : "slack_api_error";
      const status = Number.isInteger(failure.status) && failure.status >= 100 && failure.status <= 599 ? failure.status : undefined;
      return reply(502, false, "slack_delivery_not_confirmed", { stage, code, ...(status ? { upstream_status: status } : {}) });
    }
  };
}

export default { fetch: createHandler() };
