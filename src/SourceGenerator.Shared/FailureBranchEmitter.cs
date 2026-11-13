using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;

namespace NOF.SourceGeneration;

// Internal because the source is linked into separate generator assemblies and must not expose duplicate public identities.
internal static class FailureBranchEmitter
{
    public static void Emit(StringBuilder source, AttributeData[] failures, string? branchInterface = null)
    {
        foreach (var failure in failures)
        {
            var name = ExpectedFailureModel.CaseName(failure);
            var error = ExpectedFailureModel.ErrorTypeName(failure);
            var marker = SymbolDisplay.FormatLiteral(name, true);
            source.AppendLine("        /// <summary>A declared expected failure with operation-specific identity.</summary>");
            source.AppendLine("        [global::System.Text.Json.Serialization.JsonUnmappedMemberHandling(global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]");
            source.Append("        public sealed record ").Append(name)
                .AppendLine(branchInterface is null ? "\n        {" : " : " + branchInterface + "\n        {");
            if (ExpectedFailureModel.HasCustomError(failure))
            {
                source.AppendLine("            /// <summary>Gets the failure's custom error data.</summary>");
                source.AppendLine("            [global::System.Text.Json.Serialization.JsonRequired]");
                source.Append("            public ").Append(error).AppendLine(" Error { get; init { global::System.ArgumentNullException.ThrowIfNull(value); field = value; } }");
                source.AppendLine("            /// <summary>Creates the expected failure with custom error data.</summary>");
                source.AppendLine("            [global::System.Text.Json.Serialization.JsonConstructor]");
                source.Append("            public ").Append(name).Append('(').Append(error).AppendLine(" error)\n            {");
                source.AppendLine("                global::System.ArgumentNullException.ThrowIfNull(error);\n                Error = error;\n            }");
            }
            else
            {
                var code = SymbolDisplay.FormatLiteral(ExpectedFailureModel.Code(failure), true);
                source.AppendLine("            /// <summary>Gets the declared error code.</summary>");
                source.Append("            public string ErrorCode { get => ").Append(code).Append("; init { if (value != ").Append(code).AppendLine(") { throw new global::System.Text.Json.JsonException(\"Unexpected error code.\"); } } }");
                source.AppendLine("            /// <summary>Gets the failure message.</summary>");
                source.AppendLine("            [global::System.Text.Json.Serialization.JsonRequired]");
                source.AppendLine("            public string Message { get; init { global::System.ArgumentNullException.ThrowIfNull(value); field = value; } }");
                source.AppendLine("            /// <summary>Creates the expected failure with a message.</summary>");
                source.AppendLine("            [global::System.Text.Json.Serialization.JsonConstructor]");
                source.Append("            public ").Append(name).AppendLine("(string message = \"\") { global::System.ArgumentNullException.ThrowIfNull(message); Message = message; }");
            }
            source.AppendLine("            /// <summary>Gets the reserved JSON failure discriminator.</summary>");
            source.AppendLine("            [global::System.Text.Json.Serialization.JsonPropertyName(\"$failure\")]");
            source.AppendLine("            [global::System.Text.Json.Serialization.JsonRequired]");
            source.AppendLine("            public string Kind\n            {\n                get => ").Append(marker).Append(";\n                init\n                {\n                    if (value != ").Append(marker).Append(") { throw new global::System.Text.Json.JsonException(\"Unexpected failure code.\"); }\n                }\n            }\n        }");
        }
    }
}
