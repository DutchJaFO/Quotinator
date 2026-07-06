using Dapper;
using Quotinator.Data.Import;
using Quotinator.Data.Models;

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
        RegisterJsonHandler<IReadOnlyList<string>>();
        RegisterEnumHandler<ChangeAction>();
        RegisterEnumHandler<InitiatorType>();
        // DuplicateResolutionPolicy lives in Quotinator.Data.Import (not a consumer's domain), same as
        // ChangeAction/InitiatorType above — belongs here, not in a subclass's RegisterDomainHandlers().
        // Previously only registered via QuotinatorDapperConfiguration, which meant Quotinator.Data.Tests
        // (which only calls the base Configure()) could never write a SystemImportConflict row at all.
        RegisterEnumHandler<DuplicateResolutionPolicy>();
        RegisterDomainHandlers();
    }

    /// <summary>
    /// Override to register domain-specific type handlers. Called by <see cref="Configure"/> after
    /// the generic handlers are registered. Use <see cref="RegisterEnumHandler{TEnum}"/> or
    /// <see cref="RegisterJsonHandler{T}"/> rather than calling <see cref="SqlMapper"/> directly.
    /// </summary>
    protected virtual void RegisterDomainHandlers() { }

    /// <summary>
    /// Registers a <see cref="SafeEnumHandler{TEnum}"/> for <typeparamref name="TEnum"/> with Dapper.
    /// Call from <see cref="RegisterDomainHandlers"/> in subclasses.
    /// </summary>
    protected void RegisterEnumHandler<TEnum>() where TEnum : struct, Enum
        => SqlMapper.AddTypeHandler(new SafeEnumHandler<TEnum>());

    /// <summary>
    /// Registers a <see cref="JsonHandler{T}"/> for <typeparamref name="T"/> with Dapper, so any
    /// column typed as <typeparamref name="T"/> round-trips through JSON text automatically. Call
    /// once per concrete <typeparamref name="T"/> needed — e.g. from <see cref="RegisterDomainHandlers"/>
    /// when a consuming project needs to read a JSON column (such as a future typed read of
    /// <c>System_ImportConflicts.MergedFields</c> as <c>IReadOnlyDictionary&lt;string, string&gt;</c>)
    /// as something other than the raw string it's stored as today.
    /// </summary>
    protected static void RegisterJsonHandler<T>()
        => SqlMapper.AddTypeHandler(new JsonHandler<T>());
}
