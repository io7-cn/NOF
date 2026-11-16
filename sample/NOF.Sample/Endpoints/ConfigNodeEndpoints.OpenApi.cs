using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NOF.Abstraction;

namespace NOF.Sample.Endpoints;

public static partial class ConfigNodeEndpoints
{
    private static Task DescribeCreatedNodeAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var response = (OpenApiResponse)operation.Responses!["201"];
        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
        response.Headers["Location"] = new OpenApiHeader { Description = "URI of the newly created node; GET it to read the resource.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" } };
        DescribeBody(operation, "name", "A nonblank node name, 1–100 UTF-16 code units.", 1, 100);
        return Task.CompletedTask;
    }

    private static Task DescribeDeleteConflictAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        ((OpenApiResponse)operation.Responses!["409"]).Description = "HasChildren: the node cannot be deleted while it has children. error preserves NodeHasChildrenError; type is urn:nof:sample:config-node:has-children.";
        return Task.CompletedTask;
    }

    private static Task DescribeFileAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        DescribeBody(operation, "content", "A string containing a valid JSON object; arrays, scalar JSON, blank or malformed text are rejected.");
        if (operation.Parameters?.FirstOrDefault(item => item.Name == "fileName") is OpenApiParameter file)
        {
            file.Description = "A nonblank file name, 1–100 UTF-16 code units.";
            file.Schema = new OpenApiSchema { AllOf = [file.Schema!], MinLength = 1, MaxLength = 100 };
        }
        return Task.CompletedTask;
    }

    private static Task DescribeActiveFileAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        DescribeBody(operation, "fileName", "Omit or send null/empty text to clear the active file; otherwise name an existing file.");
        return Task.CompletedTask;
    }

    private static async Task DescribeStreamAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = NOFAbstractionConstants.Transport.Headers.TenantId,
            In = ParameterLocation.Header,
            Description = "Untrusted tenant intent; normal NOF authorization/tenant policy still applies.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
        {
            parameter.Description = parameter.Name switch
            {
                "count" => "Number of tick events; clamped to 1–20, not rejected outside that range.",
                "intervalMilliseconds" => "Delay between ticks in milliseconds; clamped to 100–5000.",
                "forceFailure" => "Return the declared StreamUnavailable problem before opening the stream.",
                _ => parameter.Description
            };
        }
        var payload = await context.GetOrCreateSchemaAsync(typeof(SampleStreamEvent), cancellationToken: ct);
        var media = (OpenApiMediaType)operation.Responses!["200"].Content!["text/event-stream"];
        media.Schema = null;
        media.ItemSchema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "data" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["data"] = new OpenApiSchema { Type = JsonSchemaType.String, ContentMediaType = "application/json", ContentSchema = payload, Description = "JSON SampleStreamEvent: stream-started, tick-N or stream-completed." },
                ["event"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Native SSE event name (message by default)." },
                ["id"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["retry"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
            }
        };
    }

    private static void DescribeBody(OpenApiOperation operation, string name, string description, int? minimum = null, int? maximum = null)
    {
        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var media) == true && media.Schema?.Properties?.TryGetValue(name, out var current) == true)
        {
            media.Schema.Properties[name] = new OpenApiSchema { AllOf = [current], Description = description, MinLength = minimum, MaxLength = maximum };
        }
    }
}
