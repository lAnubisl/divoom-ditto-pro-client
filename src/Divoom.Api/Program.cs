using Divoom.Api;

await using var app = ApiHost.Build(args);
await app.RunAsync();
