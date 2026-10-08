# API guidance

`ApiHost.Build` configures the application. Endpoint mappings live in `Endpoints/`;
`DeviceTransportFactory` selects the platform transport.

- Keep device operations behind the singleton `IDittoProClient`. The host owns its
  lifetime; do not create/dispose a client for each request or connect during health checks.
- Preserve the `ApiHost.Build` configuration callback so tests can replace the client.
- Require `DIVOOM_API_KEY` and `DIVOOM_ADDRESS` at startup. All routes, including
  health, require authentication through the `X-Api-Key` header.
- Preserve hashed, fixed-time credential comparison; do not accept keys in URLs
  or disclose credentials in responses or logs.
- Persist the controller UUID through `DIVOOM_STATE_DIRECTORY` and preserve the
  configured time zone and adapter settings.
- Media uploads are raw request bytes, not multipart forms. Keep the 2 MiB body
  limit, supported MIME types, pixel validation, stream rewinding, and disposal.
- Device operations share a 180-second deadline linked to request cancellation
  and application shutdown. Pass that token through client calls.
- Preserve Problem JSON error mapping and sanitized responses. Keep Bluetooth
  exceptions out of public response details.
- Keep the portable `net10.0` target usable on Linux; Windows transport references
  and selection belong behind the Windows target and conditional compilation.
- When changing routes or configuration, update `README.md` and check `Divoom.Api.Tests`.
