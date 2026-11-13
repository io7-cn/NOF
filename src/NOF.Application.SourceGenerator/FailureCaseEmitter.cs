using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NOF.SourceGeneration;
using System.Text;

namespace NOF.Application.SourceGenerator;

/// <summary>Emits the canonical result and expected failure types into an application operation.</summary>
public static class FailureCaseEmitter
{
    /// <summary>Appends result members using only Contract declarations, independent of other generators.</summary>
    public static void EmitMembers(StringBuilder source, IMethodSymbol method, string operationType)
    {
        var failures = ExpectedFailureModel.Declarations(method);
        var result = "Result";
        var resultType = operationType + ".Result";
        var success = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        FailureBranchEmitter.Emit(source, failures);
        var outcome = new StringBuilder();
        outcome.AppendLine("    /// <summary>The closed set of declared business outcomes for this operation.</summary>");
        outcome.Append("    [global::System.Text.Json.Serialization.JsonUnion(TypeClassifier = typeof(").Append(resultType).AppendLine(".JsonClassifier))]");
        outcome.Append("    public union ").Append(result).Append('(').Append(success);
        foreach (var failure in failures)
        {
            outcome.Append(", ").Append(operationType).Append('.').Append(ExpectedFailureModel.CaseName(failure));
        }
        outcome.AppendLine(")\n    {");
        outcome.AppendLine("        /// <summary>Handles every declared business outcome explicitly.</summary>");
        outcome.Append("        public TResult Match<TResult>(global::System.Func<").Append(success).Append(", TResult> onSuccess");
        for (var i = 0; i < failures.Length; i++)
        {
            outcome.Append(", global::System.Func<").Append(operationType).Append('.').Append(ExpectedFailureModel.CaseName(failures[i])).Append(", TResult> onCase").Append(ExpectedFailureModel.CaseName(failures[i]));
        }
        outcome.AppendLine(") => this switch\n        {");
        outcome.Append("            ").Append(success).AppendLine(" value => onSuccess(value),");
        for (var i = 0; i < failures.Length; i++)
        {
            outcome.Append("            ").Append(operationType).Append('.').Append(ExpectedFailureModel.CaseName(failures[i])).Append(" value => onCase").Append(ExpectedFailureModel.CaseName(failures[i])).AppendLine("(value),");
        }
        outcome.AppendLine("            null => throw new global::System.InvalidOperationException(\"The operation returned no outcome.\")\n        };");
        outcome.AppendLine("        /// <summary>Classifies generated cases without reflection or a shared open failure case.</summary>");
        outcome.AppendLine("        public sealed class JsonClassifier : global::System.Text.Json.Serialization.JsonTypeClassifierFactory\n        {");
        outcome.AppendLine("            /// <inheritdoc />");
        outcome.Append("            public override bool CanClassify(global::System.Text.Json.Serialization.JsonTypeClassifierContext context) => context.DeclaringType == typeof(").Append(resultType).AppendLine(");");
        outcome.AppendLine("            /// <inheritdoc />");
        outcome.AppendLine("            public override global::System.Text.Json.Serialization.JsonTypeClassifier CreateJsonClassifier(global::System.Text.Json.Serialization.JsonTypeClassifierContext context, global::System.Text.Json.JsonSerializerOptions options) => Classify;");
        outcome.AppendLine("            private static global::System.Type Classify(ref global::System.Text.Json.Utf8JsonReader reader)\n            {\n                var copy = reader;\n                if (copy.TokenType == global::System.Text.Json.JsonTokenType.StartObject)\n                {\n                    while (copy.Read() && copy.TokenType != global::System.Text.Json.JsonTokenType.EndObject)\n                    {\n                        if (copy.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) { throw new global::System.Text.Json.JsonException(); }\n                        var marker = copy.ValueTextEquals(\"$failure\");\n                        if (!copy.Read()) { throw new global::System.Text.Json.JsonException(); }\n                        if (marker)\n                        {\n                            if (copy.TokenType != global::System.Text.Json.JsonTokenType.String) { throw new global::System.Text.Json.JsonException(); }\n                            return copy.GetString() switch\n                            {");
        foreach (var failure in failures)
        {
            outcome.Append("                                ").Append(SymbolDisplay.FormatLiteral(ExpectedFailureModel.CaseName(failure), true)).Append(" => typeof(").Append(operationType).Append('.').Append(ExpectedFailureModel.CaseName(failure)).AppendLine("),");
        }
        outcome.AppendLine("                                _ => throw new global::System.Text.Json.JsonException(\"Undeclared failure code.\")\n                            };\n                        }\n                        copy.Skip();\n                    }\n                }");
        outcome.Append("                return typeof(").Append(success).AppendLine(");\n            }\n        }\n    }");
        foreach (var line in outcome.ToString().Split('\n'))
        {
            if (line.Length > 0)
            {
                source.Append("    ").AppendLine(line.TrimEnd('\r'));
            }
        }
    }
}
