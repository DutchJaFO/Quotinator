using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.CSharp;
using Quotinator.Api.Tests.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Stops #313 from silently regressing: no test may reach a <c>WebApplicationFactory&lt;Program&gt;</c> that
/// hands out a client before startup completes, because the symptom is usually a <em>passing</em> test
/// asserting against the startup wait page.
/// <para>
/// Two static layers judge the same rule set independently (the source analysis resolves what the code
/// refers to, the assembly analysis reads what the compiler emitted), and every code sample must get the
/// same verdict from both. Each layer is also shown to find what it should, over samples and over this
/// project itself: a guard that finds nothing proves nothing until it has been seen finding something.
/// The runtime layer, for construction no static analysis can see, is
/// <see cref="UnguardedFactoryRuntimeGuardTests"/>.
/// </para>
/// </summary>
[TestClass]
public class WebApplicationFactoryUsageGuardTests
{
    private static readonly Lazy<UnguardedFactoryUsageResult> RealProjectSource = new(() =>
        UnguardedFactorySourceAnalysis.Analyse(
            UnguardedFactoryFixtures.CompileTestProject(),
            typeof(QuotinatorWebApplicationFactory).FullName!));

    private static readonly Lazy<UnguardedFactoryUsageResult> RealProjectAssembly = new(() =>
        UnguardedFactoryAssemblyAnalysis.Analyse(typeof(QuotinatorWebApplicationFactory).Assembly, typeof(QuotinatorWebApplicationFactory)));

    /// <summary>Every sample that reaches an unguarded factory, with every kind it must be reported as.</summary>
    public static IEnumerable<object[]> ViolationFixtures =>
    [
        ["Violation-Classic",                         new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-FullyQualified",                  new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-Whitespace",                      new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedLocal",                new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedUsing",                new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedField",                new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedProperty",             new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedExpressionBodied",     new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedArgument",             new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedLambda",               new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-TargetTypedCollectionElement",    new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-Alias",                           new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-SubclassDeclared",                new[] { UnguardedFactoryUsageKind.UnguardedSubclass }],
        ["Violation-SubclassConstructed",             new[] { UnguardedFactoryUsageKind.UnguardedSubclass, UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-GenericSubclassConstructed",      new[] { UnguardedFactoryUsageKind.DirectConstruction }],
        ["Violation-NewConstraintHelper",             new[] { UnguardedFactoryUsageKind.GenericConstruction }],
        ["Violation-ActivatorGeneric",                new[] { UnguardedFactoryUsageKind.GenericConstruction }],
        ["Violation-ActivatorMethodGroup",            new[] { UnguardedFactoryUsageKind.GenericConstruction }],
        ["Violation-ActivatorTypeof",                 new[] { UnguardedFactoryUsageKind.TypeObtained }],
        ["Violation-ReflectionName",                  new[] { UnguardedFactoryUsageKind.ReflectionName }],
        ["Violation-ReflectionNameInterpolated",      new[] { UnguardedFactoryUsageKind.ReflectionName }],
    ];

    /// <summary>Every sample that must pass: the guarded factory, and the bare type named without being constructed.</summary>
    public static IEnumerable<object[]> AllowedFixtures =>
    [
        ["Allowed-GuardedClassic"],
        ["Allowed-GuardedTargetTyped"],
        ["Allowed-WithWebHostBuilderAsBareType"],
        ["Allowed-BareTypeAsParameterAndCollection"],
        ["Allowed-GuardedThroughNewConstraint"],
        ["Allowed-ConstructionInCommentAndString"],
        ["Allowed-GuardedTypeObtained"],
    ];

    /// <summary>Samples constructing the guarded factory a known number of times: the positive control for "found nothing".</summary>
    public static IEnumerable<object[]> GuardedConstructionFixtures =>
    [
        ["Allowed-GuardedClassic", 1],
        ["Allowed-GuardedTargetTyped", 2],
        ["Allowed-BareTypeAsParameterAndCollection", 1],
    ];

    [TestMethod]
    [DynamicData(nameof(ViolationFixtures))]
    public void Fixture_Violation_IsFlaggedBySourceAnalysis(string fixture, UnguardedFactoryUsageKind[] expected)
    {
        UnguardedFactoryUsageResult result = AnalyseSource(fixture);

        AssertVerdict(fixture, expected, result);
    }

    [TestMethod]
    [DynamicData(nameof(ViolationFixtures))]
    public void Fixture_Violation_IsFlaggedByAssemblyAnalysis(string fixture, UnguardedFactoryUsageKind[] expected)
    {
        UnguardedFactoryUsageResult result = AnalyseAssembly(fixture);

        AssertVerdict(fixture, expected, result);
    }

    [TestMethod]
    [DynamicData(nameof(AllowedFixtures))]
    public void Fixture_Allowed_IsNotFlaggedBySourceAnalysis(string fixture)
    {
        UnguardedFactoryUsageResult result = AnalyseSource(fixture);

        AssertVerdict(fixture, [], result);
    }

    [TestMethod]
    [DynamicData(nameof(AllowedFixtures))]
    public void Fixture_Allowed_IsNotFlaggedByAssemblyAnalysis(string fixture)
    {
        UnguardedFactoryUsageResult result = AnalyseAssembly(fixture);

        AssertVerdict(fixture, [], result);
    }

    [TestMethod]
    [DynamicData(nameof(GuardedConstructionFixtures))]
    public void Fixture_GuardedConstruction_IsCountedByBothLayers(string fixture, int expected)
    {
        Assert.AreEqual(expected, AnalyseSource(fixture).GuardedConstructionCount, $"{fixture}: source analysis");
        Assert.AreEqual(expected, AnalyseAssembly(fixture).GuardedConstructionCount, $"{fixture}: assembly analysis");
    }

    [TestMethod]
    public void RealProject_Source_CompilesWithoutErrors()
    {
        AssertInspected("this project's sources", RealProjectSource.Value);
        Assert.IsEmpty(RealProjectSource.Value.ReadFailures,
            "The source analysis could not compile this project as the build did, so what it resolved cannot be " +
            "trusted:\n" + string.Join("\n", RealProjectSource.Value.ReadFailures));
    }

    [TestMethod]
    public void RealProject_Source_FindsTheGuardedFactoryConstructions()
    {
        Assert.IsGreaterThan(0, RealProjectSource.Value.GuardedConstructionCount,
            "This project constructs QuotinatorWebApplicationFactory throughout; resolving none means the source " +
            "analysis did not see the code it reports on.");
    }

    [TestMethod]
    public void RealProject_Source_HasNoUnguardedFactoryUsage()
    {
        AssertNoFindings(RealProjectSource.Value);
    }

    [TestMethod]
    public void RealProject_Assembly_ReadsEveryMethodBody()
    {
        AssertInspected("this project's assembly", RealProjectAssembly.Value);
        Assert.IsEmpty(RealProjectAssembly.Value.ReadFailures,
            "The assembly analysis could not resolve part of this project's IL:\n" + string.Join("\n", RealProjectAssembly.Value.ReadFailures));
    }

    [TestMethod]
    public void RealProject_Assembly_FindsTheGuardedFactoryConstructions()
    {
        Assert.IsGreaterThan(0, RealProjectAssembly.Value.GuardedConstructionCount,
            "This project constructs QuotinatorWebApplicationFactory throughout; finding no construction in its IL " +
            "means the assembly analysis did not read the method bodies it reports on.");
    }

    [TestMethod]
    public void RealProject_Assembly_HasNoUnguardedFactoryUsage()
    {
        AssertNoFindings(RealProjectAssembly.Value);
    }

    /// <summary>
    /// A subclass of the guarded factory could override <c>CreateHost</c> and skip the wait while satisfying
    /// every rule above, since it is not the bare type. Sealing it removes that route.
    /// </summary>
    [TestMethod]
    public void GuardedFactory_IsSealed()
    {
        Assert.IsTrue(typeof(QuotinatorWebApplicationFactory).IsSealed,
            "QuotinatorWebApplicationFactory must be sealed, so no subclass can override its readiness wait.");
    }

    private static UnguardedFactoryUsageResult AnalyseSource(string fixture) =>
        UnguardedFactorySourceAnalysis.Analyse(UnguardedFactoryFixtures.CompileFixture(fixture), UnguardedFactoryFixtures.FixtureGuardedFactoryName);

    private static UnguardedFactoryUsageResult AnalyseAssembly(string fixture)
    {
        CSharpCompilation compilation = UnguardedFactoryFixtures.CompileFixture(fixture);
        byte[] image = UnguardedFactoryFixtures.Emit(compilation);

        AssemblyLoadContext context = new($"guard-{fixture}", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(new MemoryStream(image));
            Type guarded = assembly.GetType(UnguardedFactoryFixtures.FixtureGuardedFactoryName, throwOnError: true)!;
            return UnguardedFactoryAssemblyAnalysis.Analyse(assembly, guarded);
        }
        finally
        {
            context.Unload();
        }
    }

    private static void AssertVerdict(string fixture, UnguardedFactoryUsageKind[] expected, UnguardedFactoryUsageResult result)
    {
        Assert.IsEmpty(result.ReadFailures, $"{fixture} could not be read:\n{string.Join("\n", result.ReadFailures)}");
        AssertInspected(fixture, result);

        string found = string.Join(", ", result.Findings.Select(f => $"{f.Kind} at {f.Location}"));
        Assert.IsTrue(result.Kinds.SetEquals(expected),
            $"{fixture}: expected [{string.Join(", ", expected)}], found [{found}]");
    }

    /// <summary>
    /// The positive half of every "found nothing": the analysis examined code. Without it, an empty
    /// verdict from an analysis that read nothing would pass.
    /// </summary>
    private static void AssertInspected(string subject, UnguardedFactoryUsageResult result) =>
        Assert.IsGreaterThan(0, result.InspectedCount, $"{subject}: the analysis examined no code, so finding nothing proves nothing.");

    private static void AssertNoFindings(UnguardedFactoryUsageResult result)
    {
        AssertInspected("this project", result);
        Assert.IsEmpty(result.Findings,
            "These places reach a WebApplicationFactory<Program> that does not wait for startup:\n" +
            string.Join("\n", result.Findings.Select(f => $"{f.Kind} at {f.Location}")) + "\n\n" +
            "Use QuotinatorWebApplicationFactory instead: the bare factory returns a client before startup " +
            "completes, so requests can be answered by the startup wait page rather than the endpoint under " +
            "test (#313). Measured: the unguarded factory saw startup incomplete on 5 of 5 runs.");
    }
}
