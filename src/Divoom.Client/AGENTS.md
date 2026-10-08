# Client guidance

This portable `net10.0` library owns device commands, framing, acknowledgements,
initialization, pacing, media conversion, and retry policy.

- `IDittoProClient` is the public device API; `IDittoProTransport` is the Bluetooth
  boundary. Keep `DivoomProtocol` and wire details internal.
- `DittoProClient` owns and disposes its transport. Operations are serialized,
  connect lazily, and reuse the initialized session until failure or disposal.
- Preserve the three-attempt retry policy for transient failures: disconnect,
  reconnect, initialize, and resend the whole operation. Partial animations restart
  from the beginning. Cancellation and invalid input must not trigger retries.
- `CheckConnectionAsync` is private; do not expose it as a public operation.
- Sample current time after initialization through the configured `TimeProvider`
  and time zone. Clock display selection and time synchronization are separate commands.
- Transport notifications may arrive on any thread. Preserve copied buffers,
  checksum/sequence matching, and explicit readiness before animation upload.
- Writes are split at `min(138, transport.MaxWriteSize)`; animation upload blocks
  are 256 bytes. Keep the uint16 block-index bounds, not an assumed 255-block cap.
- Images are 16x16 RGB888. `DittoMedia` uses managed StbImageSharp decoding;
  preserve GIF timing, independent frame buffers, and portable image processing.
- Offline previews in `DittoMediaPreparation` must use the same codec as uploads.
- Validate changes with `Divoom.Client.Tests`, especially protocol vectors,
  acknowledgement handling, retries, cancellation, media, and session lifecycle.
