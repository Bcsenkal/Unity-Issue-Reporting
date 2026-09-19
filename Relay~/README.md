# Issue report relay

This Worker accepts one bounded report file, checks an upload code and rate limit, then posts the file and description to the configured Slack channel. Deploy a separate Worker for each game so its access code, Slack channel, upload limit and rate limit remain isolated.

1. Run `npm ci` in this directory.
2. Copy `wrangler.example.jsonc` to `wrangler.jsonc`. Set a unique Worker name, project display name, maximum upload bytes, and rate-limit namespace ID. The Unity upload limit must not exceed the Worker limit.
3. Set Worker secrets `SLACK_BOT_TOKEN`, `SLACK_CHANNEL_ID`, and `UPLOAD_ACCESS_CODE` with Wrangler. Use a Slack bot that can upload files to the chosen channel. Keep secret values out of source control and Unity assets.
4. Run `npm test` and `npm run check`. Deploy intentionally with `npm run deploy`, then use the resulting HTTPS `/reports` URL in the game. `GET /health` is a read-only reachability check.

The relay accepts `application/gzip`, `text/plain`, `application/json`, and `application/zip` with matching safe file extensions. It does not inspect or decompress the attachment. Limit and review the data produced by each game's attachment source. Legacy `/logs` uploads without a description remain accepted for older builds.

The optional `/diagnose` endpoint checks authentication, rate limiting, and Slack's external upload without sharing a file to the channel. It uploads only a fixed diagnostic string and returns `diagnostic_ok` on success. The `/logs` route and legacy headers exist for older clients; new integrations should use `/reports`.

Each game keeps its own Wrangler configuration and secrets. Copy or vendor this relay into the deployment project at a pinned version; Unity's Git package cache is not a stable source path for Wrangler. Updating this repository never deploys an existing Worker automatically.
