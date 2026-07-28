namespace Quotinator.Data.Import;

/// <summary>
/// Distinguishes "this JSON property was absent from the document" from "present with an explicit
/// <c>null</c> value" — the two are semantically different in an import file (absent means leave the
/// existing value alone; present-null means reset it). Domain-agnostic (see this project's Data/Core
/// project boundaries) — usable by any import shape, not just Quotinator's own entry DTOs.
/// </summary>
/// <typeparam name="T">The wrapped property's value type.</typeparam>
public readonly struct Optional<T>
{
    private Optional(bool hasValue, T? value)
    {
        HasValue = hasValue;
        Value = value;
    }

    /// <summary>
    /// <c>true</c> when the JSON property was present in the source document, regardless of whether
    /// its value was <c>null</c>. The default value of this struct (returned when the property was
    /// absent, since <see cref="System.Text.Json"/> never invokes a property's converter for a missing
    /// key) has this as <c>false</c>.
    /// </summary>
    public bool HasValue { get; }

    /// <summary>The deserialized value. Only meaningful when <see cref="HasValue"/> is <c>true</c>.</summary>
    public T? Value { get; }

    /// <summary>The default state — the property was absent from the JSON document.</summary>
    public static Optional<T> Absent { get; } = default;

    /// <summary>Wraps a present value, which may itself be <c>null</c> (an explicit reset).</summary>
    public static Optional<T> Of(T? value) => new(true, value);

    /// <summary>
    /// Treats a bare value as present — <c>Optional&lt;string&gt; x = "1994";</c> is shorthand for
    /// <c>Optional&lt;string&gt;.Of("1994")</c>. Lets callers that always mean to supply a genuine
    /// value (test fixtures, in-memory construction) write a plain value without every call site
    /// spelling out <c>Optional&lt;T&gt;.Of(...)</c>. There is deliberately no implicit conversion the
    /// other way (<c>Optional&lt;T&gt;</c> to <c>T</c>) — unwrapping must go through
    /// <see cref="OptionalExtensions.ResolveAgainst{T}"/> or an explicit <see cref="Value"/> read, since
    /// "what does this resolve to" always depends on context (an existing row, or nothing).
    /// </summary>
    public static implicit operator Optional<T>(T? value) => Of(value);
}
