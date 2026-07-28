using Quotinator.Data.Entities;

namespace Quotinator.Data.Import;

/// <summary>Builds a <see cref="FileImportReport"/> from the <see cref="SystemImportAction"/> rows one file's planning pass produced (#221).</summary>
public static class ImportActionReportBuilder
{
    /// <summary>
    /// Groups <paramref name="actions"/> by <see cref="SystemImportAction.EntityType"/> and classifies
    /// each into exactly one of 6 buckets by its <see cref="ImportActionKind"/>/<see cref="ImportActionStatus"/>
    /// pair. An entity type with no actions is omitted from the result.
    /// </summary>
    public static FileImportReport Build(string fileName, IReadOnlyList<SystemImportAction> actions)
    {
        var byEntityType = new Dictionary<string, (int New, int Modified, int Blocked, int Discarded, int Pending, int Stale)>();

        foreach (var action in actions)
        {
            var counts = byEntityType.GetValueOrDefault(action.EntityType);

            counts = action.Status.Parsed switch
            {
                ImportActionStatus.Blocked   => counts with { Blocked   = counts.Blocked + 1 },
                ImportActionStatus.Discarded => counts with { Discarded = counts.Discarded + 1 },
                ImportActionStatus.Pending   => counts with { Pending   = counts.Pending + 1 },
                ImportActionStatus.Stale     => counts with { Stale     = counts.Stale + 1 },
                ImportActionStatus.Decided or ImportActionStatus.Applied => action.ActionType.Parsed switch
                {
                    ImportActionKind.Add    => counts with { New      = counts.New + 1 },
                    ImportActionKind.Modify => counts with { Modified = counts.Modified + 1 },
                    _                       => counts,
                },
                _ => counts,
            };

            byEntityType[action.EntityType] = counts;
        }

        var entityTypes = byEntityType.ToDictionary(
            kv => kv.Key,
            kv => new EntityTypeActionCounts
            {
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
