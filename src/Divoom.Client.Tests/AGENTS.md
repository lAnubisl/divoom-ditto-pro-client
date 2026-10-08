# Client test guidance

This NUnit project targets `net10.0` and runs without Bluetooth hardware.

- `ProtocolTests` cover codec vectors; `MediaTests` cover portable decoding;
  `ClientTests` cover commands and lifecycle; `BlueZTests` cover the Linux transport.
- Use `FakeTransport` for client scenarios and `FixedClock` for time behavior.
  Preserve deterministic notification and acknowledgement simulation.
- BlueZ tests run a local D-Bus server on loopback TCP using the test endpoints
  and objects. Keep them independent of the host's system bus, BlueZ, and adapter.
- Test observable outcomes: bytes, acknowledgements, connection counts, retries,
  cancellation, session reuse, cleanup, and independent frame buffers.
- Keep async test waits bounded and dispose servers, connections, and clients.
  Do not mask races with unbounded waits or arbitrary long delays.
- Keep fixture copy paths in the project file working. Some fixtures are linked
  from root `assets/`; others live in `fixtures/`.
- New helper types must have their own matching files, including private helpers.
- Run from the repository root:
  `dotnet test src/Divoom.Client.Tests/Divoom.Client.Tests.csproj -c Release`.
