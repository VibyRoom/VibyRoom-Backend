using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace VibyRoom.Api.OpenApi;

public sealed class IfMatchHeaderOpenApiTransformer
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requiresIfMatch = context.Description
            .SupportedResponseTypes
            .Any(response =>
                response.StatusCode ==
                StatusCodes.Status428PreconditionRequired);

        if (!requiresIfMatch)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];

        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Header &&
                string.Equals(
                    parameter.Name,
                    "If-Match",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "If-Match",
            In = ParameterLocation.Header,
            Required = true,
            Description =
                "Current music state ETag. Example: \"q-1\".",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String
            }
        });

        return Task.CompletedTask;
    }
}