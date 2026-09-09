using Quotinator.Data.Entities;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Import;

/// <summary>Builds a <see cref="FileImportReport"/> from the <see cref="ImportActionEntity"/> rows one file's planning pass produced (#221).</summary>
public static class ImportActionReportBuilder
{
    /// <summary>
    /// Groups <paramref name="actions"/> by <see cref="ImportActionEntity.EntityType"/> and classifies
    /// each into exactly one of 6 buckets by its <see cref="ImportActionKind"/>/<see cref="ImportActionStatus"/>
    /// pair. An entity type with no actions is omitted from the result.
    /// </summary>
    public static FileImportReport Build(string fileName, IReadOnlyList<ImportActionEntity> actions)
    {
        var byEntityType = new Dictionary<string, (int Incoming, int New, int Unchanged, int ResolvedToExisting, int Skipped, int Modified, int Blocked, int Discarded, int Pending, int Stale)>();

        foreach (var action in actions)
        {
            var counts = byEntityType.GetValueOrDefault(action.EntityType);

            // #373: counted before the switch, so it holds even for an action matching no outcome arm
            // below. Those `_ => counts` fall-throughs discard a row silently; a total that no longer
            // equals the sum of the buckets is the only thing that makes one visible.
            counts = counts with { Incoming = counts.Incoming + 1 };

            counts = action.Status.Parsed switch
            {
                ImportActionStatus.Blocked   => counts with { Blocked   = counts.Blocked + 1 },
                ImportActionStatus.Discarded => counts with { Discarded = counts.Discarded + 1 },
                ImportActionStatus.Pending   => counts with { Pending   = counts.Pending + 1 },
                ImportActionStatus.Stale     => counts with { Stale     = counts.Stale + 1 },
                ImportActionStatus.Decided or ImportActionStatus.Applied => action.ActionType.Parsed switch
                {
                    ImportActionKind.Add                => counts with { New                = counts.New + 1 },
                    // #374/#377: a Skip-policy Modify discarded a real difference on purpose. It was
                    // already counted separately on the reseed confirmation; counting it as Modified
                    // here meant the seed log and the confirmation disagreed about the same row.
                    ImportActionKind.Modify when action.AppliedPolicy.Parsed == DuplicateResolutionPolicy.Skip
                                                        => counts with { Skipped            = counts.Skipped + 1 },
                    ImportActionKind.Modify             => counts with { Modified           = counts.Modified + 1 },
                    ImportActionKind.Unchanged          => counts with { Unchanged          = counts.Unchanged + 1 },
                    // #377: a resolution that settled on the stored values wrote nothing, so it is
                    // neither Modified (which claims a write) nor Unchanged (which says the two sides
                    // never differed).
                    ImportActionKind.ResolvedToExisting => counts with { ResolvedToExisting = counts.ResolvedToExisting + 1 },
                    _                                   => counts,
                },
                _ => counts,
            };

            byEntityType[action.EntityType] = counts;
        }

        var entityTypes = byEntityType.ToDictionary(
            kv => kv.Key,
            kv => new EntityTypeActionCounts
            {
                Incoming  = kv.Value.Incoming,
                Unchanged = kv.Value.Unchanged,
                ResolvedToExisting = kv.Value.ResolvedToExisting,
                Skipped   = kv.Value.Skipped,
                New       = kv.Value.New,
                Modified  = kv.Value.Modified,
                Blocked   = kv.Value.Blocked,
                Discarded = kv.Value.Discarded,
                Pending   = kv.Value.Pending,
                Stale     = kv.Value.Stale,
            });

        return new FileImportReport { FileName = fileName, EntityTypes = entityTypes };
    }
}
