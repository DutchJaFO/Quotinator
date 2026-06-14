using System.Reflection;

namespace Quotinator.Core.Services;

/// <summary>Provides the running assembly's version string.</summary>
public interface IVersionService
{
    /// <summary>The informational version (e.g. <c>1.0.0-beta.1</c>), without any build metadata suffix.</summary>
    string Version { get; }
}

/// <summary>Reads the version from <see cref="AssemblyInformationalVersionAttribute"/> at startup.</summary>
public sealed class VersionService : IVersionService
{
    /// <inheritdoc/>
    public string Version { get; } =
        typeof(VersionService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            // Strip +githash suffix the SDK appends when IncludeSourceRevisionInInformationalVersion is set
            ?.Split('+')[0]
        ?? "unknown";
}
