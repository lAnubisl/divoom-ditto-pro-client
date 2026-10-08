# Documentation guidance

- `README.md` is the main setup and API guide. Keep examples consistent with the
  actual routes, configuration, workflow, and Dockerfile.
- `compose.yaml` is a deployment example, copied to the Pi as `compose.yaml`.
  Document commands run from its destination directory, with `.env` alongside it.
- Deployment always pulls `panfilenok/divoom-ditto-pro-client:latest` from Docker Hub
  by default; optional `DIVOOM_IMAGE` overrides the image or tag.
  Do not add a `build:` section or restore a separate Pi Dockerfile/Compose override.
- Preserve host `/run/dbus` access, the `/data` volume, required device/key settings,
  restart policy, and bounded logs. Host BlueZ owns Bluetooth; the container uses
  the host policy's root access without privileged mode or host networking.
- Use example credentials and the configured public image name. Keep real API keys and registry tokens
  out of tracked files.
- Separate verified observations from assumptions about device rendering, clock
  readback, and persistence. Keep historical verification dates accurate.
- Check relative links after moving documentation. If Docker is available, validate
  Compose with dummy `DIVOOM_IMAGE`, `DIVOOM_ADDRESS`, and `DIVOOM_API_KEY` values
  using `docker compose -f docs/compose.yaml config` from the repository root.
