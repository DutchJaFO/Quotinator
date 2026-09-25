using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quotinator.Api.Tests.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Layer B of the #313 guard: resolves every construction, base type, generic instantiation, <c>typeof</c>
/// and string in a compilation to what it actually refers to, so no spelling can hide an unguarded factory.
/// </summary>
internal static class UnguardedFactorySourceAnalysis
{
    /// <summary>Every unguarded factory usage in <paramref name="compilation"/>.</summary>
    /// <param name="compilation">The code to analyse, compiled against the real build's references.</param>
    /// <param name="guardedFactoryMetadataName">The one factory type that may be constructed.</param>
    internal static UnguardedFactoryUsageResult Analyse(CSharpCompilation compilation, string guardedFactoryMetadataName)
    {
        List<string> readFailures = [.. compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())];

        // The open definition is named through typeof rather than a string, so this file carries neither the
        // closed bare type nor its reflection name: the two things the rules below look for.
        INamedTypeSymbol? definition = compilation.GetTypeByMetadataName(typeof(WebApplicationFactory<>).FullName!);
        INamedTypeSymbol? program    = compilation.GetTypeByMetadataName(nameof(Program));
        INamedTypeSymbol? guarded    = compilation.GetTypeByMetadataName(guardedFactoryMetadataName);
        if (definition is null || program is null || guarded is null)
        {
            readFailures.Add($"Could not resolve the factory definition ({definition is not null}), Program ({program is not null}) " +
                             $"or the guarded factory {guardedFactoryMetadataName} ({guarded is not null}).");
            return new UnguardedFactoryUsageResult { ReadFailures = readFailures };
        }

        INamedTypeSymbol bare           = definition.Construct(program);
        string           reflectionName = typeof(WebApplicationFactory<>).Name;
        IMethodSymbol?   activatorGeneric = compilation.GetTypeByMetadataName(typeof(Activator).FullName!)?
            .GetMembers(nameof(Activator.CreateInstance)).OfType<IMethodSymbol>()
            .SingleOrDefault(m => m.IsGenericMethod && m.Parameters.Length == 0);

        List<(UnguardedFactoryUsageKind Kind, string Location)> findings = [];
        int guardedConstructions = 0;
        int inspected = 0;

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                if (node is BaseObjectCreationExpressionSyntax or BaseTypeDeclarationSyntax or TypeOfExpressionSyntax
                    or GenericNameSyntax or LiteralExpressionSyntax or InterpolatedStringTextSyntax)
                {
                    inspected++;
                }

                switch (node)
                {
                    case BaseObjectCreationExpressionSyntax creation:
                        if (model.GetTypeInfo(creation).Type is INamedTypeSymbol created)
                        {
                            if (SymbolEqualityComparer.Default.Equals(created, guarded))
                            {
                                guardedConstructions++;
                            }
                            else if (IsOrDerivesFrom(created, bare))
                            {
                                findings.Add((UnguardedFactoryUsageKind.DirectConstruction, Where(node)));
                            }
                        }

                        break;

                    case BaseTypeDeclarationSyntax declaration:
                        if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol declared
                            && !SymbolEqualityComparer.Default.Equals(declared, guarded)
                            && DerivesFrom(declared, bare))
                        {
                            findings.Add((UnguardedFactoryUsageKind.UnguardedSubclass, Where(node)));
                        }

                        break;

                    case TypeOfExpressionSyntax typeOf:
                        if (SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(typeOf.Type).Type, bare))
                        {
                            findings.Add((UnguardedFactoryUsageKind.TypeObtained, Where(node)));
                        }

                        break;

                    case GenericNameSyntax generic:
                        if (model.GetSymbolInfo(generic).Symbol is IMethodSymbol method && ConstructsThroughTypeArgument(method, bare, guarded, activatorGeneric))
                        {
                            findings.Add((UnguardedFactoryUsageKind.GenericConstruction, Where(node)));
                        }

                        break;

                    case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                        if (literal.Token.ValueText.Contains(reflectionName, StringComparison.Ordinal))
                        {
                            findings.Add((UnguardedFactoryUsageKind.ReflectionName, Where(node)));
                        }

                        break;

                    case InterpolatedStringTextSyntax text:
                        if (text.TextToken.ValueText.Contains(reflectionName, StringComparison.Ordinal))
                        {
                            findings.Add((UnguardedFactoryUsageKind.ReflectionName, Where(node)));
                        }

                        break;
                }
            }
        }

        return new UnguardedFactoryUsageResult
        {
            Findings                 = findings,
            InspectedCount           = inspected,
            GuardedConstructionCount = guardedConstructions,
            ReadFailures             = readFailures,
        };
    }

    private static bool ConstructsThroughTypeArgument(IMethodSymbol method, INamedTypeSymbol bare, INamedTypeSymbol guarded, IMethodSymbol? activatorGeneric)
    {
        if (!method.IsGenericMethod)
        {
            return false;
        }

        bool isActivator = SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, activatorGeneric);
        for (int i = 0; i < method.TypeArguments.Length; i++)
        {
            bool constructs = isActivator || method.OriginalDefinition.TypeParameters[i].HasConstructorConstraint;
            if (constructs
                && method.TypeArguments[i] is INamedTypeSymbol argument
                && !SymbolEqualityComparer.Default.Equals(argument, guarded)
                && IsOrDerivesFrom(argument, bare))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOrDerivesFrom(INamedTypeSymbol type, INamedTypeSymbol bare) =>
        SymbolEqualityComparer.Default.Equals(type, bare) || DerivesFrom(type, bare);

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol bare)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, bare))
            {
                return true;
            }
        }

        return false;
    }

    private static string Where(SyntaxNode node)
    {
        FileLinePositionSpan span = node.GetLocation().GetLineSpan();
        return $"{Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}";
    }
}
