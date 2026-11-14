using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;

namespace NOF.SourceGeneration;

// Internal because this file is linked into multiple analyzer assemblies; public copies would expose conflicting type identities.
internal static class ExpectedFailureModel
{
    public static bool IsDeclaration(AttributeData attribute)
        => attribute.AttributeClass is { } type && type.ContainingNamespace.ToDisplayString() == "NOF.Contract"
            && type.Name == "FailureCaseAttribute";

    public static AttributeData[] Declarations(IMethodSymbol method) => method.GetAttributes().Where(IsDeclaration).ToArray();

    public static string OperationTypeName(IMethodSymbol method, INamedTypeSymbol applicationService)
        => applicationService.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;

    public static string ResultTypeName(IMethodSymbol method, INamedTypeSymbol applicationService) => OperationTypeName(method, applicationService) + ".Result";

    public static string CaseTypeName(IMethodSymbol method, AttributeData declaration, INamedTypeSymbol applicationService)
        => OperationTypeName(method, applicationService) + "." + CaseName(declaration);

    public static string ResponseTypeName(IMethodSymbol method, INamedTypeSymbol applicationService)
        => Declarations(method).Length > 0 ? ResultTypeName(method, applicationService)
            : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    public static bool AllowsNull(IMethodSymbol method)
        => Declarations(method).Length == 0 && (method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated
            || method.ReturnType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });

    public static string CaseName(AttributeData attribute)
        => attribute.ConstructorArguments.FirstOrDefault().Value as string ?? "";

    public static bool HasCustomError(AttributeData attribute) => attribute.ConstructorArguments.Length == 3;
    public static ITypeSymbol? ErrorType(AttributeData attribute) => HasCustomError(attribute) ? attribute.ConstructorArguments[2].Value as ITypeSymbol : null;
    public static string ErrorTypeName(AttributeData attribute) => ErrorType(attribute)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
    public static string Title(AttributeData attribute) => attribute.ConstructorArguments.Length == 1 ? CaseName(attribute) : attribute.ConstructorArguments[1].Value as string ?? "";

    public static bool ValidName(string name) => SyntaxFacts.IsValidIdentifier(name)
        && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
        && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None;
}
