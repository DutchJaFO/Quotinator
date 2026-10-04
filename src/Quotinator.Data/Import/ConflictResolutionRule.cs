using Quotinator.Data.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// Every hand-authored conflict-resolution rule (#181) for a single entity (Quote, Series, Universe, or
/// any other type that wires <see cref="ConflictRuleLookup"/> into its own planner) — one entry per
/// entity with at least one field this rule file resolves under <see cref="DuplicateResolutionPolicy.Review"/>.
/// Not limited to fields that actually disagree between the two sides: a <see cref="FieldResolutionChoice.Custom"/>
/// rule can also correct a field that's simply wrong or missing on both sides. Grouped by entity rather
/// than flattened to one entry per field, since a single entity can need more than one field corrected
/// at once. Records the two sides' <em>complete</em> field sets, not just the field(s) actually resolved
/// — a human reviewing this file needs the full record on both sides to judge whether some other,
/// not-yet-ruled field also needs attention, rather than being limited to whichever field this rule
/// file's author already knew to look at.
/// </summary>
public sealed class ConflictResolutionRule
{
    /// <summary>
    /// The entity's own id — matched case-insensitively, per this project's id-comparison convention.
    /// One rule file's <c>rules</c> array can mix ids from more than one entity type (e.g. a Quote id
    /// alongside a Series id) — ids are unique enough across types in practice that a single flat
    /// namespace needs no type discriminator, matching how <c>SystemImportAction.EntityId</c> is
    /// already used unqualified elsewhere in this codebase.
    /// </summary>
    [JsonPropertyName("entityId")]
    public required string EntityId { get; init; }

    /// <summary>
    /// The existing (already-seeded) side's complete field set, recorded at the time this rule was
    /// authored — an opaque, domain-agnostic blob (this project keeps <c>Quotinator.Data</c> free of
    /// any dependency on <c>Quotinator.Core</c>'s Quote-specific field shape) so a human reviewing this
    /// file can see the whole record, not only the field(s) actually being resolved. Purely
    /// documentation; never read by the matching logic.
    /// </summary>
    [JsonPropertyName("existingRecord")]
    public required JsonElement ExistingRecord { get; init; }

    /// <summary>
    /// The incoming (this source file's own) side's complete field set, recorded at the time this rule
    /// was authored — same shape and same human-review purpose as <see cref="ExistingRecord"/>. Purely
    /// documentation; never read by the matching logic. Staleness is judged per field, against that
    /// field's own <see cref="ConflictResolutionFieldRule.RecordedIncomingValue"/> (ADR 023). Until
    /// ADR 023 this record was what <see cref="ConflictRuleLookup.TryResolve"/> read, which is why one
    /// entity could need two entries: a snapshot recorded per entry but read per field cannot be correct
    /// for every field once an earlier rule has already enriched the incoming side.
    /// </summary>
    [JsonPropertyName("incomingRecord")]
    public required JsonElement IncomingRecord { get; init; }

    /// <summary>Every field of this entity that this rule file resolves. At least one.</summary>
    [JsonPropertyName("fields")]
    public required List<ConflictResolutionFieldRule> Fields { get; init; }
}

/// <summary>One field's resolution within a <see cref="ConflictResolutionRule"/>.</summary>
public sealed class ConflictResolutionFieldRule
{
    /// <summary>The field name this rule governs (e.g. <c>"date"</c>, <c>"type"</c>, <c>"source"</c>) — matches the consuming entity's own field-map key names (e.g. <c>QuoteFieldMerge</c> for Quote).</summary>
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    /// <summary>
    /// How to resolve this field. Keep/Replace pick one side's existing value. Custom (paired with
    /// <see cref="CustomValue"/>) sets a value neither side actually has — needed whenever the correct
    /// value is missing from both, not just disagreed upon (e.g. a quote's <c>character</c> field is
    /// <see langword="null"/> on both sides in the raw upstream data, but is actually known).
    /// </summary>
    [JsonPropertyName("resolution")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required FieldResolutionChoice Resolution { get; init; }

    /// <summary>The value to use when <see cref="Resolution"/> is <see cref="FieldResolutionChoice.Custom"/>. Ignored (and should be omitted) otherwise.</summary>
    [JsonPropertyName("customValue")]
    public string? CustomValue { get; init; }

    /// <summary>
    /// This field's own incoming value, as it stood when this rule was authored —
    /// <see cref="ConflictRuleLookup.TryResolve"/> compares it against the live import's own incoming
    /// value to decide whether the rule has gone stale, or is a candidate for retirement. Recorded per
    /// field rather than per entry (ADR 023), so one entity never needs two entries to carry two
    /// snapshots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three states are distinct and all three are load-bearing: a recorded value (a string, or a list
    /// for <c>genres</c>), an explicit <see langword="null"/> (a real recorded value — it is what the
    /// NikhilNamal17 source holds for one quote's <c>character</c>), and no recorded value at all, which
    /// resolves <see cref="ConflictRuleOutcome.Stale"/>.
    /// </para>
    /// <para>
    /// <strong>This is why the type is a non-nullable <see cref="JsonElement"/> and not a
    /// <c>JsonElement?</c></strong>, which would look like the natural choice for an optional value and
    /// is wrong: <c>System.Text.Json</c> resolves a nullable value type through its
    /// <c>NullableConverter</c>, which never calls the inner converter for a <c>null</c> token, so an
    /// explicit <c>null</c> and an absent property both deserialize to <c>HasValue == false</c> —
    /// collapsing two of the three states. The non-nullable form keeps them apart as
    /// <see cref="JsonValueKind.Null"/> and <see cref="JsonValueKind.Undefined"/>.
    /// </para>
    /// <para>
    /// <see cref="JsonIgnoreCondition.WhenWritingDefault"/> is required, not cosmetic: serializing an
    /// <see cref="JsonValueKind.Undefined"/> element throws <see cref="InvalidOperationException"/>, and
    /// <c>POST /import/rules/conflict/generate</c> writes its merged result straight back to the override
    /// file — so without it a file that legitimately omits a recorded value would fail on write.
    /// </para>
    /// </remarks>
    [JsonPropertyName("recordedIncomingValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement RecordedIncomingValue { get; init; }
}
