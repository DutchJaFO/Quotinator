namespace Quotinator.Data.Models;

/// <summary>Read model returned by the Widget-with-Owner join query — canonical example for the <c>IJoinStrategy&lt;TResult&gt;</c> pattern.</summary>
public sealed class WidgetWithOwner
{
    /// <summary>Widget primary key.</summary>
    public Guid WidgetId { get; init; }

    /// <summary>Widget display label.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Name of the owning entity.</summary>
    public string OwnerName { get; init; } = string.Empty;
}
