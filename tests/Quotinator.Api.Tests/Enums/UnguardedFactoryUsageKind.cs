namespace Quotinator.Api.Tests.Enums;

/// <summary>
/// Every way a test can reach a <c>WebApplicationFactory&lt;Program&gt;</c> that does not wait for startup
/// to complete (#313). Each static layer of the guard reports findings in these terms, so the source
/// analysis and the assembly analysis can be held to the same verdict for the same code.
/// </summary>
public enum UnguardedFactoryUsageKind
{
    /// <summary>
    /// Construction of <c>WebApplicationFactory&lt;Program&gt;</c>, or of any type deriving from it other
    /// than the guarded factory, in any spelling.
    /// </summary>
    DirectConstruction,

    /// <summary>A declared type deriving from <c>WebApplicationFactory&lt;Program&gt;</c> other than the guarded factory.</summary>
    UnguardedSubclass,

    /// <summary>
    /// The bare factory passed as a type argument to a method whose type parameter carries a <c>new()</c>
    /// constraint, or to <see cref="Activator.CreateInstance{T}()"/>.
    /// </summary>
    GenericConstruction,

    /// <summary>A <see cref="Type"/> for the bare factory obtained with <c>typeof</c>: the entry to reflection-based construction.</summary>
    TypeObtained,

    /// <summary>A string carrying the bare factory's reflection name, the route through <see cref="Type.GetType(string)"/>.</summary>
    ReflectionName,
}
