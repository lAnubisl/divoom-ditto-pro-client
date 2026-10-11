using Divoom.Api;

await using var app = ApiHost.Build(args, configure: null);
await app.RunAsync();
