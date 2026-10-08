# Project guidance

This repository controls the Divoom Ditoo Pro Light endpoint over Bluetooth.
`Divoom.Api` is the runnable application; `Divoom.Client` is the reusable protocol
library. Read `README.md` for configuration, HTTP routes, and device observations.
Directory-specific `AGENTS.md` files add guidance to these repository-wide rules.

## Project map

- `src/Divoom.Client`: portable protocol, media conversion, and client lifecycle.
- `src/Divoom.Transport.Linux`: BlueZ over host system D-Bus.
- `src/Divoom.Transport.Windows`: Windows GATT and RFCOMM/discovery support.
- `src/Divoom.Api`: authenticated ASP.NET Core API and application configuration.
- `src/Divoom.Client.Tests` and `src/Divoom.Api.Tests`: hardware-independent NUnit tests.
- `docs/compose.yaml`: example for running the Docker Hub image on the Pi.
- `.github/workflows/docker-publish.yml`: ARM64 build and Docker Hub publishing.
- `assets`: sample media used by documentation and tests.

## Repository conventions

- Every class, record, and interface must be declared in its own file, named after the type.
- Keep structs in separate files as well.
- This convention applies to production code and test helpers. Extract nested helper types into dedicated files; use the narrowest appropriate visibility and preserve public API behavior.
- Do not edit generated files in `bin/` or `obj/`, or dependencies and local experiments in `artifacts/`.

## Behavior and deployment

- Keep device protocol in the client, Bluetooth I/O in transports, and HTTP concerns in the API.
- Preserve cancellation, session reuse, and resource ownership when changing asynchronous code.
- Target .NET 10. The portable API target uses Linux BlueZ; Windows execution requires
  `net10.0-windows10.0.19041.0`.
- Raspberry Pi deployment uses Docker Hub images on 64-bit ARM64 OS. Keep the root
  `Dockerfile` for CI builds and the Compose example in `docs/`; do not reintroduce
  local Pi builds or precompiled-output Dockerfiles into the deployment instructions.
- Publishing runs only on pushes to `main` and `v*` tags. `latest` belongs to `main`.
- Keep credentials in environment configuration or GitHub Actions secrets/variables.
- Distinguish automated test results, device acknowledgements, and user-confirmed
  visual behavior. An acknowledgement does not prove rendering or persistence.

## Verification

Run from the repository root after C# refactoring:

```powershell
dotnet build src/Divoom.slnx -c Release
dotnet test src/Divoom.Client.Tests/Divoom.Client.Tests.csproj -c Release
dotnet test src/Divoom.Api.Tests/Divoom.Api.Tests.csproj -c Release
```

The full solution includes a Windows-only project. On Linux, build the portable API
with `dotnet build src/Divoom.Api/Divoom.Api.csproj -c Release -f net10.0`, then run
both test commands above, as CI does. Automated tests do not require Bluetooth hardware.
For workflow changes, use `actionlint` when available. For Compose changes, validate
with `docker compose -f docs/compose.yaml config` using dummy required environment
values. Documentation-only changes need link and instruction checks, not a C# build.
