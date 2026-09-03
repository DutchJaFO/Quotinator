namespace Quotinator.Core.Queries;

/// <summary>Read model for a single active Season reference resolved from a Source's <c>SeasonId</c>.
/// <c>Number</c> is <c>long</c>, not <c>int</c> — Dapper's record-constructor materialization requires
/// an exact type match against the reader column, and SQLite's <c>INTEGER</c> affinity always reads
/// back as <see cref="long"/> through Microsoft.Data.Sqlite, regardless of the value's magnitude or the
/// column's declared type. Property-setter mapping (the generic repository) narrows this implicitly;
/// constructor mapping (this record, used by <see cref="Quotinator.Data.Repositories.JoinQueryRepository{TResult}"/>)
/// does not — confirmed live 2026-09-03 (#375), where <c>int Number</c> here threw
/// <c>InvalidOperationException</c> on every real request. <see cref="Repositories.SourceSeasonReferenceReader"/>
/// narrows back to <c>int</c> at the point it builds the reader's own <c>int</c>-typed tuple contract.</summary>
public sealed record SourceSeasonReferenceRow(Guid Id, long Number, string? Title, string? Subtitle);
