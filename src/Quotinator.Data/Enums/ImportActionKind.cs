using Quotinator.Data.Entities;

namespace Quotinator.Data.Enums;

/// <summary>
/// The kinds of action a <see cref="ImportActionEntity"/> row can represent — a closed set defined
/// and maintained entirely by this project's own coordinator logic, not by any consuming project's
/// schema (a consumer decides, per row, which of these two kinds applies — it does not invent new
/// kinds of its own). Per ADR 008, backed by a matching SQL CHECK constraint.
/// </summary>
public enum ImportActionKind
{
    /// <summary>A brand-new record with no existing counterpart.</summary>
    Add,

    /// <summary>An existing record whose fields would change.</summary>
    Modify
}
