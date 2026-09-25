using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Builds the compilations the #313 guard analyses (this test project's own, and one per code sample)
/// from the inputs the real build handed the compiler (recorded by the
/// <c>WriteCompilationInputsForFactoryGuard</c> target in the project file). Anything less than the real
/// inputs risks a compilation that does not bind, and an analysis that binds nothing reports nothing.
/// </summary>
internal static class UnguardedFactoryFixtures
{
    /// <summary>The metadata name of the guarded factory every fixture is compiled with.</summary>
    internal const string FixtureGuardedFactoryName = "Fixture.GuardedFactory";

    private const string FixtureFolder = "UnguardedFactoryFixtures";
    private const string FixtureExtension = ".cs.txt";
    private const string SharedGuardedFactoryFile = "GuardedFactory";

    private static readonly string InputsFolder = Path.Combine(AppContext.BaseDirectory, "compilation-inputs");

    /// <summary>The test project itself, compiled exactly as the build compiled it, source generators included.</summary>
    internal static CSharpCompilation CompileTestProject()
    {
        CSharpParseOptions parseOptions = ParseOptions();
        List<SyntaxTree> trees = [.. ReadLines("sources.txt").Select(path =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))];

        // The real assembly name, not a variant of it: InternalsVisibleTo grants Quotinator.Api's internals to
        // this name only, and a compilation called anything else fails to bind every internal it touches.
        CSharpCompilation compilation = CreateCompilation(typeof(UnguardedFactoryFixtures).Assembly.GetName().Name!, trees);

        ImmutableArray<ISourceGenerator> generators = [.. ReadLines("analyzers.txt")
            .Select(path => new AnalyzerFileReference(path, AnalyzerLoader.Instance))
            .SelectMany(reference => reference.GetGenerators(LanguageNames.CSharp))];

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation generated, out _);
        return (CSharpCompilation)generated;
    }

    /// <summary>One code sample, compiled together with the fixture-local guarded factory.</summary>
    /// <param name="fixture">The sample's file name without its extension, e.g. <c>Violation-Classic</c>.</param>
    internal static CSharpCompilation CompileFixture(string fixture)
    {
        CSharpParseOptions parseOptions = ParseOptions();
        string globalUsings = ReadLines("sources.txt").Single(path => path.EndsWith(".GlobalUsings.g.cs", StringComparison.Ordinal));

        List<SyntaxTree> trees =
        [
            CSharpSyntaxTree.ParseText(File.ReadAllText(FixturePath(fixture)), parseOptions, FixturePath(fixture)),
            CSharpSyntaxTree.ParseText(File.ReadAllText(FixturePath(SharedGuardedFactoryFile)), parseOptions, FixturePath(SharedGuardedFactoryFile)),
            CSharpSyntaxTree.ParseText(File.ReadAllText(globalUsings), parseOptions, globalUsings),
        ];

        return CreateCompilation($"Fixture.{fixture}", trees);
    }

    /// <summary>
    /// Emits a compiled sample to an assembly image, so the assembly analysis reads what the compiler
    /// produced for exactly the code the source analysis read.
    /// </summary>
    /// <param name="compilation">A fixture compilation from <see cref="CompileFixture"/>.</param>
    internal static byte[] Emit(CSharpCompilation compilation)
    {
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(image);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Fixture {compilation.AssemblyName} does not compile:\n" +
                string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return image.ToArray();
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, IEnumerable<SyntaxTree> trees) =>
        CSharpCompilation.Create(
            assemblyName,
            trees,
            ReadLines("references.txt").Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static CSharpParseOptions ParseOptions() =>
        new(LanguageVersion.Latest, preprocessorSymbols: ReadLines("defines.txt")
            .SelectMany(line => line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

    private static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Startup", FixtureFolder, fixture + FixtureExtension);

    private static string[] ReadLines(string file)
    {
        string path = Path.Combine(InputsFolder, file);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The build did not record its compiler inputs at {path}. The WriteCompilationInputsForFactoryGuard " +
                "target in Quotinator.Api.Tests.csproj writes them after CoreCompile.", path);
        }

        return [.. File.ReadAllLines(path).Where(line => line.Length > 0)];
    }

    /// <summary>Loads source-generator assemblies into the default context, where Roslyn itself is already loaded.</summary>
    private sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
    {
        internal static readonly AnalyzerLoader Instance = new();

        public void AddDependencyLocation(string fullPath)
        {
        }

        public System.Reflection.Assembly LoadFromPath(string fullPath) => System.Reflection.Assembly.LoadFrom(fullPath);
    }
}
