# Windows transport guidance

This project targets `net10.0-windows10.0.19041.0` and uses Windows Bluetooth APIs.
`WindowsBleTransport` is the display transport; RFCOMM and Classic discovery support
remain separate from the BLE UART display path.

- Keep Windows dependencies in this project and the API's conditional Windows target.
- Use `DittoProUart` for service/characteristic UUIDs; preserve notification setup
  before successful connection and write-mode selection based on GATT capabilities.
- Honor cancellation in WinRT asynchronous calls. Copy notification bytes and report
  maximum writes from the GATT session's PDU size minus three.
- Release handlers, notification configuration, GATT sessions, services, and devices
  on failure and disposal. Preserve reconnect and idempotent disposal.
- Preserve native struct layouts, marshaling, and handle ownership in Classic discovery.
  Each interop struct belongs in its own file.
- Build the full solution on Windows using the root verification commands.
  The automated suites do not establish real Windows Bluetooth behavior; document
  any hardware validation separately, without claiming visual confirmation from an ACK.
