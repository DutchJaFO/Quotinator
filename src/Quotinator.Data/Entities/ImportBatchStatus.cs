namespace Quotinator.Data.Entities;

/// <summary>Lifecycle state of an <see cref="ImportBatch"/> under the staging model (#154).</summary>
public enum ImportBatchStatus
{
    /// <summary>Every planned action has been recorded; nothing has been written to any domain table yet.</summary>
    Staged,

    /// <summary>Every action has been written to its domain table. Every batch created before #154 backfills to this value.</summary>
    Applied,

    /// <summary>The batch was discarded before being applied — nothing it planned was ever written anywhere.</summary>
    Discarded
}
