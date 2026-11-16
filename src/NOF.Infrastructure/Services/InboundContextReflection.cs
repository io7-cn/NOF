using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;

namespace NOF.Infrastructure;

/// <summary>Resolves preserved operation policy metadata without invoking reflection-based handlers.</summary>
public static class InboundContextReflection
{
    /// <summary>Decodes permitted transport headers into semantic items.</summary>
    public static JsonObject ReadHeaderItems(IEnumerable<KeyValuePair<string, string?>>? headers)
        => new(headers?.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)
                && !string.Equals(pair.Key, NOFAbstractionConstants.Transport.Headers.Context, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value))) ?? [],
            new JsonNodeOptions { PropertyNameCaseInsensitive = true });

    /// <summary>Resolves the typed implementation method for policy metadata.</summary>
    public static MethodInfo ResolveHandlerMethodInfo(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type handlerType,
        Type inputType)
        => handlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == nameof(OperationHandler.HandleAsync)
                && method.GetParameters() is [{ ParameterType: var input }, { ParameterType: var context }, { ParameterType: var token }]
                && input == inputType && context == typeof(InvocationContext) && token == typeof(CancellationToken))
            ?? throw new InvalidOperationException($"No typed operation method on '{handlerType}'.");
}
