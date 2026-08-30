namespace Quotinator.Data.Notifications;

/// <summary>
/// Supplies a message key resolved in every language that defines it, so a notification producer can
/// store its title and body per language at write time (#319, #304).
/// <para>
/// Declared here rather than consumed from <c>Quotinator.Core</c>'s <c>IApiLocalizer</c>, which is the
/// implementation: notifications are Data-owned system content (ADR 018), and ADR 018's dependency edge
/// permits <c>Quotinator.Data</c> to depend only on projects that are already domain-agnostic — which
/// <c>Quotinator.Core</c> is not. Inverting the dependency is what lets the notification text builder
/// sit beside the rest of the notification machinery instead of being stranded in whichever project
/// happens to own the localisation files. <c>IApiLocalizer</c> extends this interface, so the
/// registered <c>ApiLocalizer</c> satisfies it with no separate implementation.
/// </para>
/// <para>
/// Deliberately scoped to notifications rather than declared as a general localisation abstraction. Per
/// ADR 017's reasoning, a generic contract designed against a single consumer risks being wrong in ways
/// only a second consumer reveals; if another kind of Data-owned content ever needs per-language text,
/// that is when the name and shape get revisited.
/// </para>
/// </summary>
public interface INotificationTextSource
{
    /// <summary>
    /// Returns <paramref name="key"/> resolved in every language that defines it, keyed by ISO 639-1
    /// code, with <paramref name="args"/> substituted into each language's own template.
    /// <para>
    /// Resolves no culture at all, which is the point: a startup producer writing a notification has
    /// no request culture, and the notification's text is stored per language rather than rendered
    /// per request. A language whose file lacks the key is absent from the result rather than falling
    /// back to English — a caller storing translations must be able to tell "this language has no
    /// text" from "this language's text happens to be the English one".
    /// </para>
    /// </summary>
    /// <param name="key">The message key to resolve.</param>
    /// <param name="args">Positional <c>{0}</c>/<c>{1}</c> arguments, substituted into every language.</param>
    IReadOnlyDictionary<string, string> ForEveryLanguage(string key, params object[] args);
}
