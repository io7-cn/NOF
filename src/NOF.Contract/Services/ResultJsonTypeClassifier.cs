using NOF.Abstraction;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>Distinguishes shared failures from arbitrary successful payloads without structural ambiguity.</summary>
public sealed class ResultJsonTypeClassifier : JsonTypeClassifierFactory
{
    /// <inheritdoc />
    public override bool CanClassify(JsonTypeClassifierContext context)
        => context.Kind == JsonTypeClassifierKind.Union
            && context.UnionCases.Count == 2
            && context.UnionCases.Any(static item => item.CaseType == typeof(Failure));

    /// <inheritdoc />
    public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options)
    {
        var successType = context.UnionCases.Single(static item => item.CaseType != typeof(Failure)).CaseType;
        return Classify;

        Type Classify(ref Utf8JsonReader reader)
        {
            var copy = reader;
            if (copy.TokenType == JsonTokenType.StartObject)
            {
                while (copy.Read() && copy.TokenType != JsonTokenType.EndObject)
                {
                    if (copy.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Expected a result payload property.");
                    }
                    var discriminator = copy.ValueTextEquals("$kind"u8);
                    if (!copy.Read())
                    {
                        throw new JsonException("Incomplete result payload.");
                    }
                    if (discriminator)
                    {
                        if (copy.TokenType != JsonTokenType.String || !copy.ValueTextEquals("failure"u8))
                        {
                            throw new JsonException("The reserved $kind property must identify a failure.");
                        }
                        return typeof(Failure);
                    }
                    copy.Skip();
                }
            }
            return successType;
        }
    }
}
