using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting.AspNetCore;

namespace NOF.Sample.OpenApi;

/// <summary>Owns Sample HTTP semantics that cannot be inferred from business types.</summary>
public sealed class SampleOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var binding = metadata.OfType<HttpEndpointBinding>().LastOrDefault();
        if (binding?.Operation.ContractType is { } contract)
        {
            var policy = AuthorizationPolicyResolver.Resolve(contract, binding.Operation.MethodInfo);
            if (policy.RequiresAuthentication)
            {
                operation.Security = [new() { [new OpenApiSecuritySchemeReference("Session", context.Document)] = [] },
                    new() { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
                operation.Description = (operation.Description ?? "") + " Required permission groups (AND): " +
                    string.Join("; ", policy.Requirements.Select(group => $"{group.Match}({string.Join(", ", group.Permissions)})"));
            }
        }
        if (context.Description.HttpMethod is not ("GET" or "HEAD") && metadata.OfType<IAntiforgeryMetadata>().Any(item => item.RequiresValidation))
        {
            operation.Security ??= [new()];
            if (operation.Security.Count == 0)
            {
                operation.Security.Add(new());
            }
            foreach (var requirement in operation.Security)
            {
                requirement[new OpenApiSecuritySchemeReference("CsrfHeader", context.Document)] = [];
                requirement[new OpenApiSecuritySchemeReference("CsrfCookie", context.Document)] = [];
            }
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = NOF.Sample.Identity.ExternalIdentity.AntiforgeryHeaderName,
                In = ParameterLocation.Header,
                Required = true,
                Description = "Fetch /api/demo/security-token first, retain its cookie and send the returned token.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }
        return Task.CompletedTask;
    }
}
