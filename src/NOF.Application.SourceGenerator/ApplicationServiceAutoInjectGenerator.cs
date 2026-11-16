using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace NOF.Application.SourceGenerator;

[Generator]
public sealed class ApplicationServiceAutoInjectGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidHandler = new(
        "NOF301", "Inaccessible operation handler",
        "Operation handler '{0}' must be accessible from its assembly initializer and cannot be nested in a generic type",
        "ApplicationService", DiagnosticSeverity.Error, true);
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sourceClasses = context.SyntaxProvider
            .CreateSyntaxProvider<ImmutableArray<RegistrationInfo>>(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, _) =>
                {
                    if (ctx.Node is not ClassDeclarationSyntax classDeclaration
                        || ctx.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol
                        || classSymbol.IsAbstract)
                    {
                        return [];
                    }

                    if (!ApplicationServiceSymbolHelper.TryGetGeneratedServiceOperationBaseName(
                            classDeclaration,
                            ctx.SemanticModel,
                            out var handlerBaseTypeName)
                        || handlerBaseTypeName is null)
                    {
                        for (var current = classSymbol.BaseType; current is not null; current = current.BaseType)
                        {
                            // Referenced generated Contract operations retain their original operation identity.
                            if (current.ContainingType is { } container
                                && ApplicationServiceSymbolHelper.ImplementsApplicationService(container))
                            {
                                return [new RegistrationInfo(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))];
                            }
                            if (current.OriginalDefinition.ToDisplayString() is "NOF.Application.OperationHandler<TRequest>"
                                or "NOF.Application.OperationHandler<TRequest, TResponse>")
                            {
                                if (classSymbol.IsGenericType)
                                {
                                    return [];
                                }
                                return [new RegistrationInfo(classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), current, classSymbol)];
                            }
                        }
                        return [];
                    }

                    return
                    [
                        new RegistrationInfo(
                            handlerBaseTypeName,
                            classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    ];
                })
            .Where(static registrations => !registrations.IsDefaultOrEmpty);

        var allTypesWithAssembly = context.CompilationProvider
            .Combine(sourceClasses.Collect())
            .Select((data, _) =>
            {
                var (compilation, registrationGroups) = data;
                var assemblyName = compilation.AssemblyName ?? "Unknown";
                var registrations = new List<RegistrationInfo>();
                var seen = new HashSet<string>(System.StringComparer.Ordinal);

                foreach (var registration in registrationGroups.SelectMany(x => x))
                {
                    if (!seen.Add(registration.ServiceType + "|" + registration.ImplementationType))
                    {
                        continue;
                    }

                    registrations.Add(registration);
                }

                return new RegistrationCollection(assemblyName, registrations.ToImmutableArray());
            });

        context.RegisterSourceOutput(allTypesWithAssembly, (spc, data) =>
        {
            if (data.Registrations.IsDefaultOrEmpty)
            {
                return;
            }

            var valid = data.Registrations.Where(registration =>
            {
                for (var type = registration.HandlerType; type is not null; type = type.ContainingType)
                {
                    if (type.IsGenericType || type.IsFileLocal || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(InvalidHandler, type.Locations.FirstOrDefault(), type.Name));
                        return false;
                    }
                }
                return true;
            }).ToImmutableArray();
            var source = GenerateInitializer(data.AssemblyName, valid);
            spc.AddSource("ApplicationServiceAutoInjectAssemblyInitializer.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static string GenerateInitializer(string assemblyName, ImmutableArray<RegistrationInfo> registrations)
    {
        var sanitizedName = assemblyName.Replace(".", "");
        var initializerTypeName = $"__{sanitizedName}ApplicationServiceAutoInjectAssemblyInitializer";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"[assembly: global::NOF.Abstraction.AssemblyInitializeAttribute<global::{assemblyName}.{initializerTypeName}>]");
        sb.AppendLine();
        sb.AppendLine($"namespace {assemblyName}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Registers generated services for this assembly.</summary>");
        sb.AppendLine($"    public sealed class {initializerTypeName} : global::NOF.Abstraction.IAssemblyInitializer");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <inheritdoc />");
        sb.AppendLine("        public static void Initialize(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");
        sb.AppendLine($"            if (!services.InitializedTypes.Add(typeof({initializerTypeName})))");
        sb.AppendLine("            {");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();

        foreach (var registration in registrations)
        {
            sb.AppendLine($"            services.Add(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof({registration.ServiceType}), typeof({registration.ImplementationType})));");
            if (registration.BaseType is { } handler)
            {
                var input = handler.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var output = handler.TypeArguments.Length == 1 ? "void" : handler.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var descriptorType = handler.TypeArguments.Length == 1 ? input : input + ", " + output;
                var options = "";
                if (handler.TypeArguments.Length == 2)
                {
                    var nullable = handler.TypeArguments[1].NullableAnnotation == NullableAnnotation.Annotated;
                    var cases = ApplicationServiceGenerator.BusinessCaseTypes(handler.TypeArguments[1]);
                    options = ", allowsNullResponse: " + (nullable ? "true" : "false") + ", resultCases: ["
                        + string.Join(", ", cases.Select(type => "new(typeof(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                            + "), " + (type.AllInterfaces.Any(item => item.ToDisplayString() == "NOF.Abstraction.IFailure") ? "true" : "false") + ")")) + "]";
                }
                sb.AppendLine($"            services.GetOrAddSingleton<global::NOF.Application.ApplicationServiceRegistry>().AddOperation(new(typeof({registration.ImplementationType}), typeof({input}), typeof({output})) {{ Descriptor = new global::NOF.Application.OperationDescriptor<{descriptorType}>(typeof({registration.ImplementationType}){options}) }});");
            }
            else
            {
                sb.AppendLine($"            services.GetOrAddSingleton<global::NOF.Application.ApplicationServiceRegistry>().AddOperationImplementation(typeof({registration.ServiceType}), typeof({registration.ImplementationType}));");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private sealed class RegistrationCollection
    {
        public RegistrationCollection(string assemblyName, ImmutableArray<RegistrationInfo> registrations)
        {
            AssemblyName = assemblyName;
            Registrations = registrations;
        }

        public string AssemblyName { get; }
        public ImmutableArray<RegistrationInfo> Registrations { get; }
    }

    private sealed class RegistrationInfo
    {
        public RegistrationInfo(string serviceType, string implementationType, INamedTypeSymbol? baseType = null, INamedTypeSymbol? handlerType = null)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
            BaseType = baseType;
            HandlerType = handlerType;
        }

        public string ServiceType { get; }
        public string ImplementationType { get; }
        public INamedTypeSymbol? BaseType { get; }
        public INamedTypeSymbol? HandlerType { get; }
    }
}
