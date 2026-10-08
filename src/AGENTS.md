# Source guidance

- Follow the root type-per-file convention, including test helpers and interop structs.
- Keep `Divoom.Client` independent of ASP.NET Core and platform Bluetooth APIs.
  Transport projects implement `IDittoProTransport`; the API composes the dependencies.
- Source projects generally use namespace `Divoom`; the API uses `Divoom.Api`.
  Follow the existing namespace in the file or neighboring types.
- Maintain the conditional Windows target/project references in `Divoom.Api.csproj`.
  Portable builds must not pull in Windows APIs.
- Use cancellation tokens through asynchronous operations and bounded cleanup on failure.
  Follow existing disposal ownership rather than adding duplicate cleanup calls.
- Use the root verification commands after C# refactoring. Consult the project-specific
  guidance for which existing tests cover a behavior change.
