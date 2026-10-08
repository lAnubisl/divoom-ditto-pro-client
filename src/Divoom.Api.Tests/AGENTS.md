# API test guidance

This NUnit project tests the API through a substituted `IDittoProClient` and does
not require Bluetooth hardware.

- Use `ApiHost.Build` and its configuration callback with `FakeClient` so tests
  exercise the production routing, authentication, validation, and exception handling.
- Preserve coverage for missing/incorrect keys, all route authentication, raw
  media uploads and limits, device errors, cancellation, and singleton disposal.
- Keep both TestServer coverage and real Kestrel listener coverage where relevant;
  socket-level request limits can behave differently from in-memory hosting.
- Use isolated test configuration and temporary state. Avoid depending on developer
  secrets, real device addresses, or an existing state directory.
- Dispose hosts, HTTP clients, and temporary resources; keep network waits bounded.
- Preserve linked `assets/demo.png` and `assets/mail-14.gif` fixtures in the project file.
- Keep new test helper types in dedicated files named after the type.
- Run from the repository root:
  `dotnet test src/Divoom.Api.Tests/Divoom.Api.Tests.csproj -c Release`.
