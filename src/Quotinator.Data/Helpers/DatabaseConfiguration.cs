using Dapper;

namespace Quotinator.Data.Helpers;

/// <summary>
/// Abstract base for Dapper type-handler registration. Registers generic handlers at startup
/// and exposes a hook for domain-specific registrations. Subclasses in the Engine layer override
/// <see cref="RegisterDomainHandlers"/> to add application-specific handlers without referencing
/// Dapper directly.
/// </summary>
public abstract class DatabaseConfiguration
{
    /// <summary>
    /// Registers all type handlers with the global Dapper <see cref="SqlMapper"/>. Call once at
    /// application startup, before any database query is executed.
    /// </summary>
    public void Configure()
    {
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new SafeDateHandler());
        SqlMapper.AddTypeHandler(new JsonStringListHandler());
        RegisterDomainHandlers();
    }

    /// <summary>
    /// Override to register domain-specific type handlers. Called by <see cref="Configure"/> after
    /// the generic handlers are registered. Use <see cref="RegisterEnumHandler{TEnum}"/> rather than
    /// calling <see cref="SqlMapper"/> directly.
    /// </summary>
    protected virtual void RegisterDomainHandlers() { }

    /// <summary>
    /// Registers a <see cref="SafeEnumHandler{TEnum}"/> for <typeparamref name="TEnum"/> with Dapper.
    /// Call from <see cref="RegisterDomainHandlers"/> in subclasses.
    /// </summary>
    protected void RegisterEnumHandler<TEnum>() where TEnum : struct, Enum
        => SqlMapper.AddTypeHandler(new SafeEnumHandler<TEnum>());
}
