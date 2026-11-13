using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;

namespace NOF.SourceGeneration;

// Linked into independent generators; internal avoids duplicate public type identities in consuming compilations.
internal static class FailureBranchEmitter
{
    public static void Emit(StringBuilder source, AttributeData[] failures)
    {
        foreach (var failure in failures)
        {
            var name = ExpectedFailureModel.CaseName(failure);
            var error = ExpectedFailureModel.ErrorTypeName(failure);
            var custom = ExpectedFailureModel.HasCustomError(failure);
            var title = SymbolDisplay.FormatLiteral(ExpectedFailureModel.Title(failure), true);
            var marker = SymbolDisplay.FormatLiteral(name, true);
            source.AppendLine("        /// <summary>A declared predictable failure.</summary>");
            source.AppendLine("        [global::System.Text.Json.Serialization.JsonUnmappedMemberHandling(global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]");
            source.Append("        public sealed record ").Append(name).Append(" : global::NOF.Abstraction.IFailure");
            if (custom)
            { source.Append('<').Append(error).Append('>'); }
            source.AppendLine("\n        {");
            source.AppendLine("            /// <inheritdoc />");
            source.Append("            public string Title { get => ").Append(title).Append("; init { if (value != ").Append(title).AppendLine(") { throw new global::System.Text.Json.JsonException(\"Unexpected failure title.\"); } } }");
            source.AppendLine("            /// <inheritdoc />");
            source.AppendLine("            [global::System.Text.Json.Serialization.JsonRequired]");
            source.AppendLine("            public string Detail { get; init { global::System.ArgumentNullException.ThrowIfNull(value); field = value; } }");
            if (custom)
            {
                source.AppendLine("            /// <inheritdoc />");
                source.AppendLine("            [global::System.Text.Json.Serialization.JsonRequired]");
                source.Append("            public ").Append(error).AppendLine(" Error { get; init { global::System.ArgumentNullException.ThrowIfNull(value); field = value; } }");
            }
            source.AppendLine("            /// <summary>Creates a failure with occurrence-specific detail.</summary>");
            source.AppendLine("            [global::System.Text.Json.Serialization.JsonConstructor]");
            source.Append("            public ").Append(name).Append('(');
            if (custom)
            { source.Append(error).Append(" error, "); }
            source.AppendLine("string detail = \"\")\n            {");
            source.AppendLine("                global::System.ArgumentNullException.ThrowIfNull(detail); Detail = detail;");
            if (custom)
            { source.AppendLine("                global::System.ArgumentNullException.ThrowIfNull(error); Error = error;"); }
            source.AppendLine("            }");
            source.AppendLine("            /// <summary>Gets the reserved union discriminator.</summary>");
            source.AppendLine("            [global::System.Text.Json.Serialization.JsonPropertyName(\"$failure\")]");
            source.AppendLine("            [global::System.Text.Json.Serialization.JsonRequired]");
            source.Append("            public string Kind { get => ").Append(marker).Append("; init { if (value != ").Append(marker).AppendLine(") { throw new global::System.Text.Json.JsonException(\"Unexpected failure case.\"); } } }");
            source.AppendLine("        }");
        }
    }
}
