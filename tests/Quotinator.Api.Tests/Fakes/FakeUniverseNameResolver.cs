using Quotinator.Core.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="IUniverseNameResolver"/> double, backed by a constructor-supplied
/// name → id dictionary (case-sensitive exact match, matching the real resolver's `Name = @name` SQL).
/// A name absent from the dictionary resolves to <c>null</c>, matching the real resolver's "no match"
/// contract.</summary>
internal sealed class FakeUniverseNameResolver : IUniverseNameResolver
{
    private readonly IReadOnlyDictionary<string, Guid> _idByName;

    internal FakeUniverseNameResolver(IReadOnlyDictionary<string, Guid>? seed = null)
    {
        _idByName = seed ?? new Dictionary<string, Guid>();
    }

    public Task<Guid?> ResolveIdByNameAsync(string name)
    {
        Guid? result = _idByName.TryGetValue(name, out var id) ? id : null;
        return Task.FromResult(result);
    }
}
