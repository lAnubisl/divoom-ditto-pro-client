using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Divoom.Api;

internal sealed class MediaUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath;
        if (context.ApiDescription.HttpMethod != "POST" || path is not ("api/images" or "api/animations"))
            return;

        var types = path == "api/animations"
            ? new[] { "image/gif", "application/octet-stream" }
            : new[] { "image/png", "image/jpeg", "image/bmp", "image/gif", "application/octet-stream" };
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Raw file bytes, up to 2 MiB. Select a file; do not use multipart form data.",
            Content = types.ToDictionary(type => type, _ => new OpenApiMediaType
            {
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
            })
        };
    }
}
