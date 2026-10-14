using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using VibyRoom.Api.Authentication;

namespace VibyRoom.Api.OpenApi;

public sealed class AntiforgeryHeaderOpenApiTransformer
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var method = context.Description.HttpMethod;
        var path = context.Description.RelativePath;

        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(
                "api/",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(method))
        {
            return Task.CompletedTask;
        }

        var requiresAntiforgery =
            HttpMethods.IsPost(method) ||
            HttpMethods.IsPut(method) ||
            HttpMethods.IsPatch(method) ||
            HttpMethods.IsDelete(method);

        if (!requiresAntiforgery)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];

        var headerName = GuestAntiforgeryExtensions.HeaderName;

        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Header &&
                string.Equals(
                    parameter.Name,
                    headerName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = headerName,
            In = ParameterLocation.Header,
            Required = true,
            Description =
                "Antiforgery token obtained from " +
                "GET /api/v1/security/antiforgery.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String
            }
        });

        return Task.CompletedTask;
    }
}