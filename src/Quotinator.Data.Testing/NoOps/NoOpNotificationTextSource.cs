using Quotinator.Data.Notifications;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>
/// No-op <see cref="INotificationTextSource"/> for tests that construct a notification producer but do
/// not exercise its text — resolves every key to the key itself, in English only.
/// <para>
/// Returning the key rather than an empty dictionary is deliberate: a producer that stores an empty body
/// would violate the <c>NOT NULL</c> on <c>Body</c> and fail for a reason unrelated to whatever the test
/// is asserting. Wording and its per-language resolution belong to tests that use the real locale files.
/// </para>
/// </summary>
public sealed class NoOpNotificationTextSource : INotificationTextSource
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpNotificationTextSource Instance = new();

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> ForEveryLanguage(string key, params object[] args)
        => new Dictionary<string, string> { ["en"] = key };
}
