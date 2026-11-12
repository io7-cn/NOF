using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NOF.Domain;
using NOF.Domain.SourceGenerator;
using System.Collections.Immutable;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public sealed class ValueObjectNormalizeAnalyzerTests
{
    private static readonly Type[] _refs =
    [
        typeof(IValueObject<>),
        typeof(NewableValueObjectAttribute),
        typeof(IdGenerator)
    ];

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var extraReferences = _refs.Select(t => t.ToMetadataReference()).ToArray();
        var compilation = CSharpCompilation.CreateCompilation("TestAssembly", source, true, extraReferences);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ValueObjectNormalizeAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task LegacyValidate_ReportsMigrationError()
    {
        var diagnostics = await GetDiagnosticsAsync("""
            using NOF.Domain;
            public readonly partial struct Name : IValueObject<string>
            {
                public static void Validate(string value) { }
            }
            """);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF019" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("From(value)")]
    [InlineData("TryFrom(value, out _)")]
    public async Task IsValid_CannotReenterConstruction(string call)
    {
        var diagnostics = await GetDiagnosticsAsync($$"""
            using NOF.Domain;
            public readonly partial struct Name : IValueObject<string>
            {
                public static bool IsValid(string value) { {{call}}; return true; }
            }
            """);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF013" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Normalize_CallingFrom_ReportsNOF013()
    {
        const string source = """
            using NOF.Domain;
            namespace Test;

            public readonly partial struct TenantId : IValueObject<string>
            {
                public static string Normalize(string value) => (string)From(value);
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Contains(diagnostics, d => d.Id == "NOF013" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Normalize_CallingIsValid_ReportsNOF014()
    {
        const string source = """
            using NOF.Domain;
            namespace Test;

            public readonly partial struct TenantId : IValueObject<string>
            {
                public static string Normalize(string value)
                {
                    IsValid(value);
                    return value.Trim();
                }

                public static bool IsValid(string value) => true;
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Contains(diagnostics, d => d.Id == "NOF014" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Normalize_WithPureNormalization_DoesNotReportDiagnostics()
    {
        const string source = """
            using NOF.Domain;
            namespace Test;

            public readonly partial struct TenantId : IValueObject<string>
            {
                public static string Normalize(string value) => value.Trim().ToLowerInvariant();
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id is "NOF013" or "NOF014");
    }
}
