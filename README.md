# Divoom Web API

Authenticated Web API for the Divoom Ditoo Pro: images, looping GIF animations
and date/time over Bluetooth. Supports Windows BLE and Linux BlueZ, including
Docker on Raspberry Pi. `Divoom.Api` is the application entry point;
`Divoom.Client` can also be used directly from .NET applications.

## Contents

- [Projects and architecture](#projects-and-architecture)
- [Build and test](#build-and-test)
- [Linux host setup](#linux-host-setup)
- [Raspberry Pi and Docker](#raspberry-pi-and-docker)
- [Run without Docker](#run-without-docker)
- [HTTP API](#http-api)
- [Configuration](#configuration)
- [Client API](#client-api)
- [Linux transport](#linux-transport)
- [Device verification](#device-verification)
- [References and licenses](#references-and-licenses)

## Projects and architecture

- `Divoom.Api`: routing, authentication, validation, configuration and a shared
  `IDittoProClient` for the host's lifetime. Operations live in `Endpoints`.
- `Divoom.Client`: protocol, acknowledgements, initialization, lazy connection,
  session reuse and disposal through `IDittoProTransport`, plus managed
  PNG/JPEG/BMP/GIF decoding and resizing to 16x16.
- `Divoom.Transport.Windows`: Windows GATT/RFCOMM channels and discovery.
- `Divoom.Transport.Linux`: BlueZ BLE/GATT channel through host system D-Bus.
- `Divoom.Client.Tests`: protocol, media, lifecycle and local D-Bus tests.
- `Divoom.Api.Tests`: HTTP authentication, validation, errors, lifetime and Kestrel tests.

Source projects are under `src/`; sample media are under `assets/`.
`Divoom.Client` includes `DittoMedia` and the StbImageSharp dependency for
portable file and stream decoding.

One `IDittoProClient` lives for the API host's lifetime. It connects lazily,
serializes device operations and reuses its session. Stopping the API disposes
the client and transport. HTTP routing stays in `Divoom.Api`, while protocol
and connection logic live in the client and transports.

The portable API target `net10.0` uses Linux BlueZ. On Windows, use the
`net10.0-windows10.0.19041.0` target to select `WindowsBleTransport`.
The client and Linux transport also target `net10.0` and support Linux ARM64.

## Build and test

Install .NET SDK 10 and run these commands from the repository root.
The NUnit test projects run with `dotnet test` and support IDE test discovery.

```powershell
dotnet build src/Divoom.slnx -c Release
dotnet test src/Divoom.Client.Tests/Divoom.Client.Tests.csproj -c Release
dotnet test src/Divoom.Api.Tests/Divoom.Api.Tests.csproj -c Release
```

Client tests cover protocol vectors, media decoding, connection lifecycle and
an actual local D-Bus server over loopback TCP. API tests use a fake device
client and cover authentication, uploads, limits, errors, singleton disposal
and a real Kestrel listener. Automated tests do not require Bluetooth hardware.

## Linux host setup

Use a 64-bit Raspberry Pi OS with Bluetooth enabled and BlueZ running on the
host. Select the Ditoo **Light** endpoint. The transport looks up the MAC address
on the specified adapter and starts a bounded discovery session if it is unknown.
It stops its own discovery session after finding the device or on failure.

```sh
sudo systemctl enable --now bluetooth
bluetoothctl show
```

If GATT reports an authentication error or service resolution times out, pair
the device on the host. Release any phone connection first. Start an interactive
`bluetoothctl` session and enter:

```text
agent NoInputNoOutput
default-agent
pair B1:21:81:4B:E6:42
trust B1:21:81:4B:E6:42
quit
```

Wait for `Pairing successful` before quitting. The agent is needed during initial
pairing; the host keeps the resulting bond for later container connections.
For a different adapter, set `DIVOOM_ADAPTER=hci1`. To keep the selection stable
when adapter indexes change after reboot, use its controller MAC address from
`bluetoothctl list`, for example `DIVOOM_ADAPTER=00:1A:7D:DA:71:11`.

## Raspberry Pi and Docker

After publishing the image with [GitHub Actions](#publish-to-docker-hub-with-github-actions)
and setting up host Bluetooth, copy [docs/compose.yaml](docs/compose.yaml) to
the Pi as `compose.yaml`. With Docker
Compose installed, run from the directory containing that file:

```sh
export DIVOOM_ADDRESS=B1:21:81:4B:E6:42
export DIVOOM_API_KEY="$(openssl rand -hex 32)"
export DIVOOM_IMAGE=panfilenok/divoom-ditto-pro-client:latest

docker compose pull
docker compose up -d
```

Keep the generated key for HTTP requests. You can also store these variables
in a local `.env` file for Compose. Changing the key requires recreating the
container. `DIVOOM_PORT` sets the published host port (default 8080). Compose
publishes it on the host's network interfaces. For Internet access, use an
HTTPS reverse proxy to encrypt the key in transit.

GitHub Actions uses the root `Dockerfile` to build `Divoom.Api` and publish it
to Docker Hub. The Pi pulls the image without compiling the application.
The .NET 10 images support ARM64 on 64-bit Raspberry Pi OS.
The [Compose example](docs/compose.yaml) mounts `/run/dbus` read-only so the
container can communicate with host BlueZ, and a named volume at `/data`
persists the controller UUID across runs. `TZ` defaults to `Europe/Amsterdam`.

The container runs as root for the default system-bus BlueZ policy. A non-root
deployment needs a corresponding host D-Bus policy. No privileged container,
host networking, USB device mapping or second Bluetooth daemon is required.
BlueZ owns the radio on the host.

### Publish to Docker Hub with GitHub Actions

`.github/workflows/docker-publish.yml` builds a `linux/arm64` image for 64-bit
Raspberry Pi OS using GitHub's native ARM64 runner and the root `Dockerfile`.
It builds the portable API and runs both test projects before building the image.
32-bit Raspberry Pi OS is not supported by this workflow.

Create a Docker Hub repository, then configure these values in your GitHub
repository under **Settings > Secrets and variables > Actions**:

| Kind | Name | Value |
| --- | --- | --- |
| Variable | `DOCKERHUB_USERNAME` | Your Docker Hub login username (`panfilenok`) |
| Secret | `DOCKERHUB_TOKEN` | Docker Hub access token with Read & Write permission for the image repository |
| Variable (optional) | `DOCKERHUB_IMAGE` | Override the full image name; defaults to `panfilenok/divoom-ditto-pro-client` |

The workflow runs only on pushes to `main` and tags matching `v*`.
Pushes to `main` publish `latest` and a
`sha-<commit>` tag. Pushing a semantic version tag such as `v1.2.3` publishes
`1.2.3`, `1.2`, and a commit tag; it does not replace `latest`.

On the Pi, run from the directory containing your copied `compose.yaml`,
configure the device address and API key as above, and select the published image:

```sh
export DIVOOM_IMAGE=panfilenok/divoom-ditto-pro-client:latest
docker compose pull
docker compose up -d
```

`DIVOOM_IMAGE` defaults to `panfilenok/divoom-ditto-pro-client:latest`, so setting
it is optional. You can store it in `.env` alongside the other settings. Repeat the
two Compose commands to update the container, or choose a version tag such as
`panfilenok/divoom-ditto-pro-client:1.2.3` to use a specific release.
Containers restart after reboot and log files are limited to three files of 10 MB.

## Run without Docker

Without Docker on Linux, set the same variables and run:

```sh
export DIVOOM_ADDRESS=B1:21:81:4B:E6:42
export DIVOOM_API_KEY="$(openssl rand -hex 32)"

dotnet run --project src/Divoom.Api -f net10.0 -- --urls http://0.0.0.0:8080
```

On Windows, the Windows target selects `WindowsBleTransport` automatically:

```powershell
$env:DIVOOM_ADDRESS = 'B1:21:81:4B:E6:42'
$env:DIVOOM_API_KEY = '<your-api-key>'
dotnet run --project src/Divoom.Api -f net10.0-windows10.0.19041.0 -- --urls http://127.0.0.1:8080
```

Windows state defaults to `%LOCALAPPDATA%\Divoom.Api`; Linux defaults to `/data`.

## HTTP API

All routes, including health, require an `X-Api-Key` header. The API reads the
key from `DIVOOM_API_KEY`. Startup fails if the key or device address is missing.
Run the following examples from the repository root.

| Method | Route | Body |
| --- | --- | --- |
| GET | `/api/health` | None; API liveness without Bluetooth connection |
| POST | `/api/images` | Raw PNG, JPEG, BMP or GIF bytes (first GIF frame) |
| POST | `/api/animations` | Raw animated GIF bytes |
| POST | `/api/display/clock` | None; select the built-in clock display |
| POST | `/api/time/current` | None; current time in configured `TZ` |
| POST | `/api/time` | JSON: `{"value":"2026-10-06T12:34:56+02:00"}` |

Uploads use raw file bytes, not multipart form data. Set `Content-Type` to the
image MIME type or `application/octet-stream`. Images resize to 16x16; GIF frame
delays are preserved. The key is accepted only in the header, never in the URL.

To switch displays, upload an image or animation, or call `/api/display/clock`.
Clock selection returns `{"status":"sent","mode":"clock"}` after device
acknowledgement. It does not set the time or change the power-on default mode.
The `/api/time` endpoints synchronize time without selecting the clock display.
To return to an image or animation after showing the clock, resend its file.
Previously uploaded device slots and persistence are not assumed.

```sh
API=http://raspberrypi.local:8080
curl --fail-with-body -H "X-Api-Key: $DIVOOM_API_KEY" "$API/api/health"

# Animated envelope with the number 14
curl --fail-with-body -X POST \
  -H "X-Api-Key: $DIVOOM_API_KEY" -H "Content-Type: image/gif" \
  --data-binary @assets/mail-14.gif "$API/api/animations"

curl --fail-with-body -X POST \
  -H "X-Api-Key: $DIVOOM_API_KEY" -H "Content-Type: image/png" \
  --data-binary @assets/demo.png "$API/api/images"

curl --fail-with-body -X POST \
  -H "X-Api-Key: $DIVOOM_API_KEY" "$API/api/time/current"

curl --fail-with-body -X POST \
  -H "X-Api-Key: $DIVOOM_API_KEY" "$API/api/display/clock"

curl --fail-with-body -X POST \
  -H "X-Api-Key: $DIVOOM_API_KEY" -H "Content-Type: application/json" \
  --data '{"value":"2026-10-06T12:34:56+02:00"}' "$API/api/time"
```

Success: HTTP 200, JSON `{"status":"sent"}`. Image responses also include
`width`/`height`; animation responses include `frames`. This means the client
completed the acknowledged operation, not visual inspection of the display.
Health returns `{"status":"ok"}`.

Errors use Problem JSON: 401 for absent/invalid keys, 400 for invalid input,
413 for bodies over 2 MiB, 415 for unsupported media types, 503 for unavailable
Bluetooth and 504 for timeouts. Responses omit private transport exception
details. Input images have a 1,048,576-pixel limit per frame; animations also
obey the client's native upload size limit. Operations have a 180-second deadline
and cancel when the request aborts or the host stops.

## Configuration

| Variable | Default / purpose |
| --- | --- |
| `DIVOOM_IMAGE` | Compose image and tag; defaults to `panfilenok/divoom-ditto-pro-client:latest` |
| `DIVOOM_API_KEY` | Required API key |
| `DIVOOM_ADDRESS` | Required Light endpoint MAC address |
| `DIVOOM_PORT` | 8080; Compose host port |
| `DIVOOM_ADAPTER` | `hci0`; accepts an adapter index or controller MAC address |
| `TZ` | `Europe/Amsterdam` |
| `DIVOOM_STATE_DIRECTORY` | `/data` |
| `DBUS_SYSTEM_BUS_ADDRESS` | `unix:path=/run/dbus/system_bus_socket` |

Each HTTP device operation has a 180-second deadline shared by all retry
attempts. Operations cancel when the request aborts or the host stops.
Bluetooth I/O failures and timeouts retry up to three total attempts, waiting
one second after disconnecting between attempts. Each retry reconnects and
resends the entire operation; a lost acknowledgement may cause duplicate
delivery. Cancellation stops retries.

## Client API

Portable .NET 10 client for the Divoom Ditoo Pro BLE display endpoint.
The library includes portable image/GIF decoding through StbImageSharp and
accepts file paths or streams through `DittoMedia`.

`IDittoProClient` is the public contract for all device operations and inherits
`IAsyncDisposable`. `DittoProClient` implements it; applications can depend on
the interface for dependency injection or substitution.

```csharp
using Divoom;

// The client owns and disposes the injected IDittoProTransport.
await using IDittoProClient client = new DittoProClient(transport, new DittoProClientOptions
{
    ControllerId = persistedControllerId,
    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")
});

await client.SendImageAsync(new DittoImage(rgb888)); // 768 bytes, row-major
await client.SendAnimationAsync(new[]
{
    new DittoAnimationFrame(firstImage, TimeSpan.FromMilliseconds(200)),
    new DittoAnimationFrame(secondImage, TimeSpan.FromMilliseconds(500))
});
await client.SendCurrentDateTimeAsync();
await client.ShowClockAsync(); // Select the built-in clock display.
```

`Divoom.Client` includes portable file decoding through `DittoMedia`:

```csharp
await client.SendImageAsync(DittoMedia.LoadImage("picture.png"));
await client.SendAnimationAsync(DittoMedia.LoadGif("animation.gif"));
```

### Connection lifecycle

The first send method connects and initializes the device lazily. Successful
operations share this connection and session until `DisposeAsync`, which
disconnects and disposes the owned transport. Concurrent operations are
serialized; sequence numbers and image counters continue within the session.
Disposal delegates to the transport's `DisposeAsync`, which owns disconnecting
and releasing its resources; the client does not issue a second disconnect.
All methods accept cancellation tokens. A failed or cancelled in-flight
operation closes the uncertain session. Bluetooth I/O failures and timeouts
automatically retry the complete operation, with at most three total attempts
and a one-second pause after cleanup between attempts. Each retry reconnects
and initializes a new session; partial animations restart from the beginning.
The final failure is propagated. Cancellation and invalid input are not retried;
cancellation also interrupts the retry delay. Cancellation before an operation
starts preserves the existing connection. A lost acknowledgement can cause
content that was already accepted to be sent again.

`CheckConnectionAsync` is private and reserved for future client self-diagnostics.
Connection checks are not exposed through the public client API.
An internal successful connection check also keeps the channel open until disposal.

### Media and protocol behavior

`DittoMediaPreparation` exposes offline quantized previews and prepared image/
animation sizes. Applications do not access the internal wire codec.

Current time is sampled after initialization through `TimeProvider`, then
converted to `TimeZone` (host local by default). Initialization sends JSON
SetUTC and binary time synchronization. `SendDateTimeAsync(DateTimeOffset)`
can send an explicit wall-clock value. Host NTP synchronization is not verified.
The binary year fields are `year % 100` and `year / 100` (decimal century).

`ShowClockAsync` sends the BLE clock channel command (`45 00`) and waits for
its sequence acknowledgement. It selects the live display independently of
time synchronization and does not change the startup channel. Resend an image
or animation to display that content again. Physical rendering is not confirmed
by the acknowledgement.
`TIME_SENT` logs the sampled timestamp. A time-command acknowledgement confirms
receipt; device clock readback/display verification is not available on the
tested firmware. The extended `BD 2E` read-time query did not return a time reply.

Images preserve up to 128 colors, quantize larger palettes, and use a rolling
preamble counter. Solid colors use Lighting. A checksum-valid sequence response
confirms image receipt, not rendering. Animation frames have individual 1..65535
ms durations and concatenated AA framing in upload chunks of 256 bytes.
There is no 255-block / approximately 64 KiB cap. The uint16 block index permits
up to 65,536 blocks (16 MiB); this is a representation boundary, not a verified
device capacity. The firmware's actual maximum animation size is unknown.
The 0x8b announce carries the exact uint32 file length; each upload block carries
that length and a uint16 block index. Explicit device start permission is
required before upload. No extra file prefix or commit trailer is sent.
The reserved `slot` argument must be zero. Animation completion means all transport writes
completed; display rendering and power-cycle persistence are not inferred.

### Platform boundary

`IDittoProTransport` handles connection, notification subscription, ordered byte
writes, disconnection and maximum ATT value size. UUIDs are in `DittoProUart`.
The client owns framing, checksums, initialization, pacing, splitting at
`min(138, MaxWriteSize)` and response parsing.

- `Divoom.Transport.Windows` implements Windows GATT, Classic RFCOMM connection
  diagnostics and endpoint discovery. The Web API constructs a transport and
  calls the portable client API.
- `Divoom.Transport.Linux` implements `BlueZBleTransport` through host system
  D-Bus. It resolves the Light UART service, subscribes with `StartNotify`,
  forwards characteristic `Value` changes and writes with ATT response flow
  control. See [Linux host setup](#linux-host-setup).

## Linux transport

`BlueZBleTransport` implements `IDittoProTransport` using the host's BlueZ
system D-Bus API. It handles Bluetooth discovery, GATT connection, notification
subscription, writes and cleanup. Device commands, packet splitting and session
initialization remain in `DittoProClient`.

```csharp
using Divoom;

await using IDittoProClient client = new DittoProClient(
    new BlueZBleTransport("B1:21:81:4B:E6:42", new BlueZTransportOptions
    {
        Adapter = "hci0",
        BusAddress = "unix:path=/run/dbus/system_bus_socket"
    }),
    new DittoProClientOptions
    {
        ControllerId = persistedControllerId,
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")
    });

await client.SendAnimationAsync(DittoMedia.LoadGif("assets/mail-14.gif"));
await client.SendCurrentDateTimeAsync(); // Reuses the same initialized connection.
// Disposing the client disposes the transport and closes Bluetooth once.
```

### Behavior and validation

- Connect waits for `Connected` and `ServicesResolved`, resolves the UART UUIDs
  beneath the selected device's service and installs signal subscriptions before
  calling `StartNotify`. It completes only after notifications are enabled.
- `WriteValue` uses `type=request` for ATT flow control. A UART without the
  `write` flag is rejected rather than silently using unacknowledged writes.
- The negotiated characteristic `MTU`, when exposed by BlueZ, gives a maximum
  write size of `MTU - 3`; otherwise the conservative maximum is 20 bytes. The
  client independently splits packets at a maximum of 138 bytes.
- Notification `Value` arrays are copied; remote disconnect/service invalidation
  or a D-Bus connection error marks the channel unavailable. The client owns
  reconnection and protocol reinitialization after a failure.
- StopNotify, device Disconnect and D-Bus release run on disposal/failure.
  Cleanup uses a separate bounded token after setup cancellation. No protocol
  command or write is automatically retried by this transport.
- Tests run an actual local D-Bus server over loopback TCP, without requiring
  BlueZ hardware. They cover wire types, subscriptions, writes, discovery,
  cancellation, errors, negotiated/fallback MTU, reconnect and idempotent disposal.

## Device verification

### Windows â€” 2026-10-06

On 2026-10-06 the Windows-target API was tested against DitooPro-Light at
`B1:21:81:4B:E6:42`. Two authenticated HTTP requests to `/api/time/current`
returned 200 with device acknowledgements. The second reused the initialized
session (about 0.3 seconds versus 9.3 seconds for the first). The test host was
stopped afterwards. Device clock readback was not verified.

The same Windows session also sent a test image and `assets/mail-14.gif`
(9 frames). The user confirmed animated playback after API shutdown; earlier
static images and native animations were also visually confirmed.

After a power cycle the device displayed the clock. Automatic animation
restoration at startup is not implemented; file persistence is unknown.
The extended `BD 2E` clock query timed out on the tested firmware.

### Raspberry Pi â€” 2026-10-07

The implementation and Web API have been built and tested on Windows, with a
cross-publish for `linux-arm64`. On 2026-10-07 the published application was run
in Docker on a Raspberry Pi 3 Model B with Debian 13 ARM64. After host pairing,
the time command and a nine-frame animation returned HTTP 200. The time command
was acknowledged by the device. Physical display rendering and clock readback
were not visually verified.
Earlier reconnect tests were intermittent. After updating the device address to
`B1:21:81:4B:E6:43`, replacing the power cable/supply, removing a metal heatsink,
and positioning the Pi about one metre from Divoom, BLE RSSI improved to
-64..-72 dBm and `vcgencmd get_throttled` returned `0x0`. Time and animation
requests passed after a host reboot and again after a container restart; a
repeated animation also passed. The effects of the individual physical changes
were not isolated. Visual playback from the Pi still needs user confirmation.

## References and licenses

[BlueZ GATT API](https://bluez.readthedocs.io/en/latest/gatt-api/),
[BlueZ Device API](https://bluez.readthedocs.io/en/latest/device-api/),
[BlueZ Adapter API](https://bluez.readthedocs.io/en/latest/adapter-api/),
[Tmds.DBus modelling](https://github.com/tmds/Tmds.DBus/blob/main/docs/modelling.md).

Protocol observations and initialization derive from
[orionparrott/divoom-protocol](https://github.com/orionparrott/divoom-protocol).
The MIT license and dependency notices are included in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
