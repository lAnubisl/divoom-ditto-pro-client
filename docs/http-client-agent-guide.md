# Divoom HTTP integration guide for a community gallery client

This is a standalone handoff for the agent building the new gallery application.
Copy this file into that project's documentation and reference it from its
`AGENTS.md`. It describes the HTTP API implemented by this repository.

## Instructions for the application agent

Build a client application that browses the community-maintained Divoom online
gallery and sends selected artwork to an already-running Divoom HTTP API on the
network. Treat that API as the complete device-control boundary.

- Use HTTP for all device operations. Do not reference `Divoom.Client` or either
  transport project, implement device packets, or manage Bluetooth, pairing,
  discovery, adapters, or device addresses in this application.
- Keep gallery access and device API access in separate services. Gallery
  credentials, identifiers, metadata, and downloaded artwork belong to the
  gallery integration. Device API requests contain ordinary media files or the
  documented time JSON.
- Configure the device API base URL and API key. The server operator provisions
  the device; this application does not need its MAC address or server setup.
- Send raw media bytes with the correct content type. Let the API resize and
  prepare the media for the 16x16 display.
- Preserve cancellation and serialize sends per configured API server so user
  actions have predictable ordering. Keep a reusable HTTP client.
- Present HTTP success as a completed send. Do not claim verified display
  rendering, clock readback, stored artwork, or power-cycle persistence.
- Test HTTP behavior using a mock server; automated client tests must not require
  physical hardware.

The community gallery's API, authentication, download formats, and usage terms
are outside this device API contract. Verify those separately against the
chosen gallery service. Do not invent gallery endpoints or assume a gallery
artwork identifier can be submitted directly to the device API.

## Connection configuration

Suggested application settings (these names are a client convention):

| Setting | Purpose | Example |
| --- | --- | --- |
| `DIVOOM_API_BASE_URL` | Reachable server origin, optionally with a reverse-proxy path prefix; exclude `/api` | `http://raspberrypi.local:8080` |
| `DIVOOM_API_KEY` | Shared key provided by the API server operator | Secret from environment or secure storage |

Append the routes below to the configured base URL, preserving any proxy path
prefix. Use HTTPS for Internet access. All requests, including health checks,
must include exactly one header:

```http
X-Api-Key: <configured-api-key>
```

The key is not accepted in a URL or query parameter. Do not log it, commit it,
embed it in a public frontend bundle, or forward it to gallery download URLs.
Keep separate HTTP clients or request-scoped authentication for the gallery
and device API.

For a browser-based application, use an application backend to hold the key
and forward device requests. This API does not configure CORS; do not assume
direct cross-origin browser calls will work.

## HTTP contract

All successful operations return HTTP `200` with JSON. There is no asynchronous
job identifier or polling endpoint; the device request stays open until the
operation completes or fails.

| Method and route | Request | Success JSON |
| --- | --- | --- |
| `GET /api/health` | No body | `{"status":"ok"}` |
| `POST /api/images` | Raw PNG, JPEG, BMP, or GIF file bytes | `{"status":"sent","width":16,"height":16}` |
| `POST /api/animations` | Raw GIF file bytes | `{"status":"sent","frames":9}` (frame count varies) |
| `POST /api/display/clock` | Optional JSON with `style` and `color`; no body uses defaults | `{"status":"sent","mode":"clock"}` |
| `POST /api/time/current` | No body | `{"status":"sent"}` |
| `POST /api/time` | JSON with `value`, an ISO 8601 timestamp with UTC offset | `{"status":"sent"}` |

Health checks confirm API reachability and authentication. They do not connect
to or check the physical device. A healthy API can still fail a device send.

### Media requests

Uploads are raw binary bodies, **not** multipart, JSON, base64, file paths,
remote URLs, or gallery IDs.

| Endpoint | Accepted `Content-Type` values |
| --- | --- |
| `/api/images` | `image/png`, `image/jpeg`, `image/bmp`, `image/gif`, `application/octet-stream` |
| `/api/animations` | `image/gif`, `application/octet-stream` |

Always set `Content-Type`. `application/octet-stream` still requires supported
file contents; it does not enable other media formats.

- Maximum request body: **2 MiB (2,097,152 bytes)**.
- Maximum input image/frame size: **1,048,576 pixels** (width multiplied by height).
- The server resizes media to **16x16** and performs device color preparation.
  Prefer suitable pixel art when available; do not assume large source images
  will retain detail.
- `/api/images` uses the first frame of a GIF. Use `/api/animations` for playback
  of an animated GIF; frame delays are preserved.
- Animations also undergo a server-side prepared-upload size check. A compressed
  GIF below 2 MiB may still fail validation. Actual device animation capacity
  is not established; do not promise every accepted source will render.
- Convert unsupported gallery formats, such as WebP, APNG, video, or proprietary
  artwork data, into a supported image or GIF before uploading.

### Clock and time

`POST /api/display/clock` accepts an optional JSON body:

```json
{"style":0,"color":"#00FF00"}
```

`style` is an integer from 0 to 15. `color` is an RGB hex string in `#RRGGBB`
format (hex digits are case-insensitive). Each omitted field defaults independently:
style `0`, green `#00FF00`. Existing requests without a body remain supported and
use the same defaults. The success JSON remains `{"status":"sent","mode":"clock"}`.
Invalid settings return HTTP 400 without sending device commands; JSON bodies
require `Content-Type: application/json` (unsupported content types return 415).
Clock selection uses 24-hour format and disables weather, temperature and calendar.
Local user inspection on 2026-10-10 confirmed green digits on a dark background
for style `0`; style `14` produced a blank screen. Do not assume every accepted
style/color combination renders or survives power cycling.

`/api/time/current` uses the API server's current time and configured timezone
(`TZ`, default `Europe/Amsterdam`). It does not use the gallery application's
local clock or timezone.

For an explicit wall-clock value, send:

```http
POST /api/time
X-Api-Key: <configured-api-key>
Content-Type: application/json

{"value":"2026-10-08T12:34:56+02:00"}
```

Always provide an explicit UTC offset (or `Z`). Synchronizing time does not
select the clock display. To synchronize and show the clock, await
`POST /api/time/current`, then await `POST /api/display/clock`.

Clock selection does not change the device's startup display. To return to
artwork, resend its image or GIF. Cache the source file or retain a downloadable
gallery reference in the application; there is no API to restore an upload by ID.

## Gallery-to-device flow

1. Browse/search the gallery using its own integration and credentials.
2. Download the selected artwork using the gallery's media URL or export API.
   Never attach the device API key to that request.
3. Determine whether the selection should be a still image or animation. Obtain
   or convert to a supported file, and check its size before sending. Do not
   mistake an HTML/error download response for image bytes.
4. POST the bytes to `/api/images` or `/api/animations` with `X-Api-Key` and an
   accepted `Content-Type`. Await the HTTP response.
5. Show the outcome and keep the selection/source available for resend.

There are no device API routes for gallery browsing, remote URL fetching,
device discovery, multiple-device selection, playlists, stored slots, display
readback, brightness, power, or deleting uploaded content. Each configured API
server controls its configured device. Implement gallery browsing, favorites,
history, caching, and any scheduling in the new application.

## Deadlines, cancellation, and failures

The server gives each device operation a **180-second deadline**, shared by
its internal attempts. Configure the HTTP client and any application proxy to
allow longer than that (for example, **210 seconds**). The first send may take
longer than later sends. Cancellation of the HTTP request also cancels the
server operation when the server detects the disconnect.

The server serializes device operations and retries eligible device I/O failures
and timeouts up to **three total attempts**. Avoid automatic overlapping POST
retries in the gallery client. A failed, cancelled, or disconnected request can
have an uncertain device outcome; a retry can send the same content again.
Offer a deliberate resend, or use bounded sequential retries if the product
requires them. Do not retry invalid requests unchanged.

| HTTP status | Client handling |
| --- | --- |
| `400` | Invalid/empty media, excessive pixel count, invalid time JSON, or another input validation failure; fix the input. |
| `401` | Missing or invalid API key; check the device API credentials. |
| `413` | Request body exceeds 2 MiB; reduce or replace the media. |
| `415` | Missing/unsupported content type; use the documented MIME type. |
| `503` | Device unavailable; show a device-service failure and allow a later resend. |
| `504` | Device operation timed out or service is stopping; outcome may be uncertain. |
| `499` | Request cancellation, if a response can still be delivered; commonly the client instead observes local cancellation. |
| `500` | Unexpected server error; show a service failure. |

Errors generally use Problem JSON (`application/problem+json`) with fields such
as `title` and `status`. Do not assume every error has this shape: missing/null
`value` at `/api/time` returns `400` with
`{"error":"value is required (ISO 8601 with UTC offset)."}`, and framework or
proxy errors can have empty or different bodies. Check the HTTP status first,
then parse the body defensively. Handle network/DNS errors and local timeout
or cancellation separately from HTTP errors. Server responses omit private
device exception details.

## Manual HTTP examples

PowerShell examples use a reachable, already-configured server and local artwork
files. Supply the real key through environment configuration.

```powershell
$env:DIVOOM_API_BASE_URL = 'http://raspberrypi.local:8080'
# DIVOOM_API_KEY must already be set to the server operator's shared key.
$headers = @{ 'X-Api-Key' = $env:DIVOOM_API_KEY }
$apiBase = $env:DIVOOM_API_BASE_URL.TrimEnd('/')

Invoke-RestMethod -Uri "$apiBase/api/health" -Headers $headers

Invoke-RestMethod -Method Post -Uri "$apiBase/api/images" `
    -Headers $headers -ContentType 'image/png' -InFile './artwork.png' `
    -TimeoutSec 210

Invoke-RestMethod -Method Post -Uri "$apiBase/api/animations" `
    -Headers $headers -ContentType 'image/gif' -InFile './artwork.gif' `
    -TimeoutSec 210

Invoke-RestMethod -Method Post -Uri "$apiBase/api/time/current" `
    -Headers $headers -TimeoutSec 210
Invoke-RestMethod -Method Post -Uri "$apiBase/api/display/clock" `
    -Headers $headers -TimeoutSec 210
```

## Client acceptance checks

Use mock HTTP responses and representative media to verify authentication,
route selection, binary uploads, size rejection, response parsing, cancellation,
timeouts, and handling of the statuses above. Verify that gallery requests never
receive the device API key. Test send ordering and returning from clock to
artwork by resending the cached file.

With a real server, report authenticated health, HTTP send results, and
user-confirmed visual playback separately. HTTP success confirms completion
of the server operation; it does not prove rendering or persistence.
