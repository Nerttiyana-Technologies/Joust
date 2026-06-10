using System.Collections.Concurrent;

namespace Joust.Mapping;

/// <summary>
/// Registry of source-generated <see cref="IRowMapper{T}"/> implementations. Generated code
/// registers a mapper for each annotated record type at module-initialization time; the core then
/// prefers the registered mapper over the default compiled-delegate one.
/// </summary>
public static class MapperRegistry
{
    private static readonly ConcurrentDictionary<Type, object> Mappers = new();

    /// <summary>Registers a mapper for <typeparamref name="T"/>.</summary>
    public static void Register<T>(IRowMapper<T> mapper) where T : class =>
        Mappers[typeof(T)] = mapper;

    /// <summary>Gets the registered mapper for <typeparamref name="T"/>, or null.</summary>
    public static IRowMapper<T>? Get<T>() where T : class =>
        Mappers.TryGetValue(typeof(T), out var m) ? (IRowMapper<T>)m : null;

    /// <summary>Returns true if a mapper is registered for the given type.</summary>
    public static bool IsRegistered(Type type) => Mappers.ContainsKey(type);
}
