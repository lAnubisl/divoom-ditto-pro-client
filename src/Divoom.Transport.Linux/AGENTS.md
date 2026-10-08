# Linux transport guidance

`BlueZBleTransport` implements `IDittoProTransport` using Tmds.DBus and the host's
BlueZ service. `IBlueZBus` separates transport behavior from D-Bus calls.

- Preserve the D-Bus interface names, method signatures, and wire property types.
  The local D-Bus tests exercise these signatures, not just fake method calls.
- Resolve the selected adapter by `hci` index or controller MAC address. Keep
  discovery bounded and stop discovery started by this transport during cleanup.
- Connect must wait for both `Connected` and `ServicesResolved`, find the UART
  characteristics, install signal subscriptions, and enable notifications before returning.
- Require the characteristic's `write` capability and `WriteValue` with
  `type=request`. Preserve ATT flow control.
- Report write capacity as negotiated MTU minus three, or 20 bytes when unavailable.
- Copy notification bytes; remote disconnects, invalidated service state, and
  subscription failures must invalidate the connection. Ignore stale session signals.
- Preserve bounded cleanup and disposal of late subscriptions when cancellation
  wins a race. Disconnect and disposal release notifications, device connection,
  discovery, subscriptions, and bus resources; reconnect must remain supported.
- Keep command retries in `DittoProClient`, not in transport writes.
- Containers use the host `/run/dbus` socket and BlueZ daemon. Do not introduce a
  second daemon or require privileged containers for the documented deployment.
- Validate using `BlueZTests` in `Divoom.Client.Tests`; hardware validation is a
  separate activity and must be reported separately.
