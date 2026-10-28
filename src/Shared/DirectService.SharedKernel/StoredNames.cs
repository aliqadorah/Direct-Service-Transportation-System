using System.Collections.Concurrent;
using System.Reflection;

namespace DirectService.SharedKernel;

/// <summary>
/// The entity's name as stored in data (platform restructuring design §e, E-1): <c>core.AuditLogs.EntityType</c>, record locks, live
/// refresh events, import batches, custom/standard field configuration, history links and the company export all use it. Pinned to the
/// value the namespace + class name gave before the restructuring (e.g. <c>TRN.Trip</c>), so moving or renaming the class never changes it.
/// Never edit a value: the golden tests in <c>tests/DirectService.Architecture.Tests</c> compare every one with the restore tag.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StoredNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// The label that derives the entity's URL token key (HKDF info <c>"DST-URL:" + label</c>, design §e, E-3). Pinned to the class name it had
/// before the restructuring: changing it changes every URL id of that type (bookmarks, emailed and notification links, saved report filters).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TokenLabelAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}

/// <summary>Reads the pinned names of an entity type; a type without its pin is a programming error, never a computed fallback.</summary>
public static class StoredNames
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();
    private static readonly ConcurrentDictionary<Type, string> Labels = new();

    /// <summary>The stored name, e.g. <c>TRN.Trip</c>.</summary>
    public static string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Names.GetOrAdd(type, t => t.GetCustomAttribute<StoredNameAttribute>(false)?.Name
            ?? throw new InvalidOperationException($"{t.FullName} has no [StoredName]: every entity needs its pinned stored name (design §e, E-1)."));
    }

    /// <summary>The stored name without its module prefix, e.g. <c>Trip</c> (the form used by source and party type columns).</summary>
    public static string ShortOf(Type type)
    {
        var name = Of(type);
        return name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..];
    }

    /// <summary>The URL token label, e.g. <c>Trip</c>.</summary>
    public static string TokenLabelOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Labels.GetOrAdd(type, t => t.GetCustomAttribute<TokenLabelAttribute>(false)?.Label
            ?? throw new InvalidOperationException($"{t.FullName} has no [TokenLabel]: every entity needs its pinned URL token label (design §e, E-3)."));
    }

    /// <summary>True when the type carries a stored name (an entity of the data model).</summary>
    public static bool IsPinned(Type type) => type?.GetCustomAttribute<StoredNameAttribute>(false) is not null;
}
