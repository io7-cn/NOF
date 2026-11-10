using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Linq;
using System.Text;

namespace NOF.Contract.SourceGenerator;

/// <summary>Emits the server-side binder and JSON body codec beside a generated HTTP client.</summary>
public static class HttpRpcRouteEmitter
{
    public static string GetName(ServiceMethodInfo method, int index)
        => $"__NofHttpRoute_{method.OperationName}_{index}";

    public static string BuildClientUri(string route, HttpRpcRouteBinding binding, string requestType)
    {
        var segments = route.Split('/');
        return string.Join(" + \"/\" + ", segments.Select(segment =>
        {
            if (segment.Length > 2 && segment[0] == '{' && segment[segment.Length - 1] == '}')
            {
                var property = binding.Path.First(item => item.Name == segment.Substring(1, segment.Length - 2)).Property;
                var value = $"(({requestType})currentRequest).@{property.Name}";
                var formatted = property.Type.ToDisplayString() == "System.Guid"
                    ? value + ".ToString(\"D\")"
                    : value + ".ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
                return $"global::System.Uri.EscapeDataString({formatted})";
            }

            return Literal(segment);
        }));
    }

    public static void Emit(
        StringBuilder sb, ServiceMethodInfo method, EndpointInfo endpoint, HttpRpcRouteBinding binding, string name, string route)
    {
        var service = TypeName(method.Method.ContainingType);
        var request = TypeName(method.RequestType!);
        var response = method.ReturnInfo.ClientResponseTypeDisplay;
        var body = $"__NofHttpBody_{method.OperationName}";
        var usesQuery = endpoint.Method is HttpVerb.Get or HttpVerb.Delete;

        sb.AppendLine("        // Assembly-local so the generated wire codec cannot become a consumer API.");
        sb.AppendLine($"        internal sealed class {name} : global::NOF.Contract.GeneratedHttpRpcRoute<{service}, {response}>");
        sb.AppendLine("        {");
        sb.AppendLine($"            public {name}() {{ }}");
        sb.AppendLine($"            public override string OperationName => nameof({service}.@{method.OperationName});");
        sb.AppendLine($"            public override string Template => {Literal(route)};");
        sb.AppendLine($"            public override global::NOF.Contract.HttpVerb Verb => global::NOF.Contract.HttpVerb.{endpoint.Method};");
        sb.AppendLine("            public override global::System.Collections.Generic.IReadOnlyList<global::NOF.Contract.HttpRpcRouteParameter> Parameters { get; } =");
        sb.AppendLine("            [");
        foreach (var (parameter, property) in binding.Path)
        {
            sb.AppendLine($"                new({Literal(parameter)}, typeof({TypeName(property.Type)}), IsPath: true, IsRequired: true),");
        }

        if (usesQuery)
        {
            foreach (var property in binding.Remaining)
            {
                var required = !HttpRpcRouteBinding.IsOptionalQuery(property);
                sb.AppendLine($"                new({Literal(property.Name)}, typeof({TypeName(HttpRpcRouteBinding.GetQueryValueType(property.Type))}), IsPath: false, IsRequired: {Boolean(required)}),");
            }
        }

        sb.AppendLine("            ];");
        sb.AppendLine($"            public override bool HasBody => {Boolean(binding.HasBody)};");
        sb.AppendLine($"            public override global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetBodyTypeInfo(global::System.Text.Json.JsonSerializerOptions options) => {(binding.HasBody ? "GetJsonBody(options)" : "null")};");
        sb.AppendLine();
        EmitBinder(sb, request, body, binding, usesQuery);
        if (binding.HasBody)
        {
            EmitBodyCodec(sb, body, request, binding);
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static void EmitBinder(StringBuilder sb, string request, string body, HttpRpcRouteBinding binding, bool usesQuery)
    {
        var asyncModifier = binding.HasBody ? "async " : "";
        var invalid = binding.HasBody ? "null" : "default";
        sb.AppendLine($"            public override {asyncModifier}global::System.Threading.Tasks.ValueTask<object?> BindAsync(");
        sb.AppendLine("                global::System.Func<string, string?> getPath, global::System.Func<string, string?> getQuery,");
        sb.AppendLine("                global::System.Func<global::System.Text.Json.Serialization.Metadata.JsonTypeInfo, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<object?>> readBody,");
        sb.AppendLine("                global::System.Text.Json.JsonSerializerOptions options,");
        sb.AppendLine("                global::System.Threading.CancellationToken cancellationToken)");
        sb.AppendLine("            {");
        for (var i = 0; i < binding.Path.Count; i++)
        {
            var (parameter, property) = binding.Path[i];
            sb.AppendLine($"                if (!TryParse<{TypeName(property.Type)}>(getPath({Literal(parameter)}), out var path{i}))");
            EmitInvalid(sb, 16, invalid);
        }

        if (usesQuery)
        {
            for (var i = 0; i < binding.Remaining.Count; i++)
            {
                EmitQueryRead(sb, binding.Remaining[i], i);
            }
        }

        if (binding.HasBody)
        {
            sb.AppendLine($"                if (await readBody(GetJsonBody(options), cancellationToken).ConfigureAwait(false) is not {body} payload)");
            EmitInvalid(sb, 16, invalid);
        }

        sb.AppendLine($"                var request = new {request}");
        sb.AppendLine("                {");
        for (var i = 0; i < binding.Path.Count; i++)
        {
            sb.AppendLine($"                    @{binding.Path[i].Property.Name} = path{i},");
        }

        for (var i = 0; i < binding.Remaining.Count; i++)
        {
            var property = binding.Remaining[i];
            if (usesQuery || property.IsRequired || property.SetMethod!.IsInitOnly)
            {
                var value = usesQuery ? $"query{i}" : $"payload.Value{i}";
                sb.AppendLine($"                    @{property.Name} = {value}!,");
            }
        }

        sb.AppendLine("                };");
        if (binding.HasBody)
        {
            for (var i = 0; i < binding.Remaining.Count; i++)
            {
                var property = binding.Remaining[i];
                if (!property.IsRequired && !property.SetMethod!.IsInitOnly)
                {
                    sb.AppendLine($"                if (payload.HasValue{i})");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    request.@{property.Name} = payload.Value{i}!;");
                    sb.AppendLine("                }");
                }
            }
        }

        sb.AppendLine(binding.HasBody ? "                return request;" : "                return new(request);");
        sb.AppendLine("            }");
        sb.AppendLine();
    }

    private static void EmitQueryRead(StringBuilder sb, IPropertySymbol property, int index)
    {
        var type = HttpRpcRouteBinding.GetQueryValueType(property.Type);
        var optional = HttpRpcRouteBinding.IsOptionalQuery(property);
        var text = $"text{index}";
        var value = $"query{index}";
        sb.AppendLine($"                var {text} = getQuery({Literal(property.Name)});");
        if (type.SpecialType == SpecialType.System_String)
        {
            sb.AppendLine($"                var {value} = {text};");
            if (!optional)
            {
                sb.AppendLine($"                if ({value} is null)");
                EmitInvalid(sb, 16, "default");
            }
            return;
        }

        var parser = type.TypeKind == TypeKind.Enum
            ? $"global::System.Enum.TryParse<{TypeName(type)}>({text}, ignoreCase: true, "
            : $"TryParse<{TypeName(type)}>({text}, ";
        if (!optional)
        {
            sb.AppendLine($"                if (!{parser}out var {value}))");
            EmitInvalid(sb, 16, "default");
            return;
        }

        sb.AppendLine($"                {TypeName(type)}? {value} = default;");
        sb.AppendLine($"                if ({text} is not null)");
        sb.AppendLine("                {");
        sb.AppendLine($"                    if (!{parser}out var parsed{index}))");
        EmitInvalid(sb, 20, "default");
        sb.AppendLine($"                    {value} = parsed{index};");
        sb.AppendLine("                }");
    }

    private static void EmitBodyCodec(StringBuilder sb, string body, string request, HttpRpcRouteBinding binding)
    {
        sb.AppendLine($"            private sealed class {body}");
        sb.AppendLine("            {");
        for (var i = 0; i < binding.Remaining.Count; i++)
        {
            var property = binding.Remaining[i];
            sb.AppendLine($"                public {TypeName(property.Type)} Value{i} = default!;");
            if (!property.IsRequired && !property.SetMethod!.IsInitOnly)
            {
                sb.AppendLine($"                public bool HasValue{i};");
            }
        }

        sb.AppendLine("            }");
        sb.AppendLine($"            private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{body}>> __NofBodyMetadata = new();");
        sb.AppendLine();
        sb.AppendLine($"            public static global::System.Net.Http.HttpContent CreateBody({request} request, global::System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine("            {");
        sb.AppendLine($"                var body = new {body}");
        sb.AppendLine("                {");
        for (var i = 0; i < binding.Remaining.Count; i++)
        {
            sb.AppendLine($"                    Value{i} = request.@{binding.Remaining[i].Name},");
        }

        sb.AppendLine("                };");
        sb.AppendLine("                return global::System.Net.Http.Json.JsonContent.Create(body, GetJsonBody(options));");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{body}> GetJsonBody(global::System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine("                => __NofBodyMetadata.GetValue(options, static settings =>");
        sb.AppendLine($"                    global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateObjectInfo(settings, new global::System.Text.Json.Serialization.Metadata.JsonObjectInfoValues<{body}>");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        ObjectCreator = static () => new {body}(),");
        sb.AppendLine("                        PropertyMetadataInitializer = _ => CreateJsonProperties(settings)");
        sb.AppendLine("                    }));");
        sb.AppendLine();
        sb.AppendLine("            private static global::System.Text.Json.Serialization.Metadata.JsonPropertyInfo[] CreateJsonProperties(global::System.Text.Json.JsonSerializerOptions options)");
        sb.AppendLine("            {");
        sb.AppendLine($"                var properties = new global::System.Text.Json.Serialization.Metadata.JsonPropertyInfo[{binding.Remaining.Count}];");
        for (var i = 0; i < binding.Remaining.Count; i++)
        {
            EmitJsonProperty(sb, body, binding.Remaining[i], i);
        }

        sb.AppendLine("                return properties;");
        sb.AppendLine("            }");
    }

    private static void EmitJsonProperty(StringBuilder sb, string body, IPropertySymbol property, int index)
    {
        var type = TypeName(property.Type);
        var trackPresence = !property.IsRequired && !property.SetMethod!.IsInitOnly;
        sb.AppendLine($"                properties[{index}] = global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreatePropertyInfo(options,");
        sb.AppendLine($"                    new global::System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues<{type}>");
        sb.AppendLine("                    {");
        sb.AppendLine("                        IsProperty = true,");
        sb.AppendLine("                        IsPublic = true,");
        sb.AppendLine($"                        DeclaringType = typeof({body}),");
        sb.AppendLine($"                        PropertyName = {Literal(property.Name)},");
        sb.AppendLine($"                        Getter = static obj => (({body})obj).Value{index},");
        sb.AppendLine("                        Setter = static (obj, value) =>");
        sb.AppendLine("                        {");
        sb.AppendLine($"                            var body = ({body})obj;");
        sb.AppendLine($"                            body.Value{index} = value!;");
        if (trackPresence)
        {
            sb.AppendLine($"                            body.HasValue{index} = true;");
        }
        sb.AppendLine("                        },");

        if (HttpRpcRouteBinding.FindJsonAttribute(property, "JsonPropertyNameAttribute")?.ConstructorArguments[0].Value is string jsonName)
        {
            sb.AppendLine($"                        JsonPropertyName = {Literal(jsonName)},");
        }
        if (HttpRpcRouteBinding.GetJsonIgnoreCondition(property) is int ignore)
        {
            sb.AppendLine($"                        IgnoreCondition = (global::System.Text.Json.Serialization.JsonIgnoreCondition){ignore},");
        }
        if (HttpRpcRouteBinding.FindJsonAttribute(property, "JsonNumberHandlingAttribute")?.ConstructorArguments[0].Value is int numbers)
        {
            sb.AppendLine($"                        NumberHandling = (global::System.Text.Json.Serialization.JsonNumberHandling){numbers},");
        }

        sb.AppendLine("                    });");
        if (property.IsRequired || HttpRpcRouteBinding.FindJsonAttribute(property, "JsonRequiredAttribute") is not null)
        {
            sb.AppendLine($"                properties[{index}].IsRequired = true;");
        }
        if (HttpRpcRouteBinding.FindJsonAttribute(property, "JsonPropertyOrderAttribute")?.ConstructorArguments[0].Value is int order)
        {
            sb.AppendLine($"                properties[{index}].Order = {order};");
        }
    }

    private static void EmitInvalid(StringBuilder sb, int indent, string result)
    {
        var prefix = new string(' ', indent);
        sb.AppendLine(prefix + "{");
        sb.AppendLine(prefix + $"    return {result};");
        sb.AppendLine(prefix + "}");
    }

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
}
