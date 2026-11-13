using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NOF.Contract;
using NOF.Contract.SourceGenerator;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public sealed class OperationDescriptorGeneratorTests
{
    [Fact]
    public void PlainContract_NeedsNeitherPartialNorApplicationReference()
    {
        const string source = """
            using NOF.Contract;
            public sealed record SendMail(string To);
            public interface IMailService : IApplicationService
            {
                Empty Send(SendMail request);
                string? Find(SendMail request);
            }
            public static class Producer
            {
                public static ServiceOperationDescriptor<SendMail, Empty> Target => IMailServiceOperations.Send;
            }
            """;
        var compilation = CSharpCompilation.CreateCompilation("PlainContractDescriptors", source, true,
            typeof(IApplicationService), typeof(ServiceOperationDescriptor<,>));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new OperationDescriptorGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        Assert.Empty(diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
        Assert.Empty(generated.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error));
        var code = generated.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("partial interface", code);
        Assert.DoesNotContain("NOF.Application", code);
        Assert.Contains("allowsNullResponse: true", code);
    }
}
