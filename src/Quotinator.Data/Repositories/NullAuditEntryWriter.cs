using System.Data;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>
/// A production (not test-only) null-object <see cref="IAuditEntryWriter"/> for repositories whose
/// own database structurally has no <c>Audit_Entry</c> table to write to — currently the separate,
/// in-memory changelog database (#309, ADR 018), which is deliberately isolated from the main
/// database's own audit infrastructure along with everything else domain-coupled. <see
/// cref="SqliteRepository{T}"/> writes its audit entry using the *same* connection passed to it, so
/// supplying the real <see cref="IAuditEntryWriter"/> against the changelog's own connection factory
/// would attempt <c>INSERT INTO Audit_Entry</c> against a database that has no such table.
/// </summary>
/// <remarks>
/// Named <c>NullAuditEntryWriter</c>, not <c>NoOpAuditEntryWriter</c> — deliberately distinct from
/// <c>Quotinator.Data.Testing.NoOps.NoOpAuditEntryWriter</c>, the test-only equivalent
/// (<c>Quotinator.Data.Testing</c> may only be referenced from test projects, never production code,
/// per this project's own convention). The two names being close was found to genuinely collide: a
/// test file importing both <c>Quotinator.Data.Repositories</c> and
/// <c>Quotinator.Data.Testing.NoOps</c> could no longer resolve either type unqualified.
/// </remarks>
public sealed class NullAuditEntryWriter : IAuditEntryWriter
{
    /// <summary>Shared instance — this class carries no state, so there is never a reason to construct more than one.</summary>
    public static readonly NullAuditEntryWriter Instance = new();

    /// <inheritdoc/>
    public Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(AuditEntryEntity entry) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ClearAsync(string? table = null) => Task.CompletedTask;
}
