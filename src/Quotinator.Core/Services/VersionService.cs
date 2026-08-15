using System.Reflection;

namespace Quotinator.Core.Services;

/// <summary>Provides the running application's identity — which application, and which version.</summary>
public interface IVersionService
{
    /// <summary>The informational version (e.g. <c>1.0.0-beta.1</c>), without any build metadata suffix.</summary>
    string Version { get; }

    /// <summary>
    /// The running application's name (e.g. <c>Quotinator.Api</c>). Kept separate from
    /// <see cref="Version"/>, never concatenated with it. This is the *actual* entry assembly rather
    /// than a hardcoded product name, because more than one application can legitimately open the same
    /// database — <c>Quotinator.Tools.DbInspector</c> is an existing example — and recording which one
    /// did is the point of storing it at all (#312).
    /// </summary>
    string Application { get; }
}

/// <summary>Reads application name and version from the entry assembly at startup.</summary>
public sealed class VersionService : IVersionService
{
    /// <inheritdoc/>
    public string Version { get; } =
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            // Strip +githash suffix the SDK appends when IncludeSourceRevisionInInformationalVersion is set
            ?.Split('+')[0]
        ?? "unknown";

    /// <inheritdoc/>
    public string Application { get; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
}
