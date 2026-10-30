using static DirectService.SharedKernel.AuditWords;

namespace DirectService.SharedKernel;

/// <summary>
/// The vocabulary of every module (<see cref="IAuditVocabularyProvider"/>s) for the shared audit describer (Stage 2 session 9, DT-329): the
/// first module that has words for a record type, action, field or code gives them; hooks are asked module by module. SharedKernel since
/// Stage 2 session 14 (the platform's audit log reads the same words).
/// </summary>
public sealed class AuditVocabularies(IEnumerable<IAuditVocabularyProvider> providers)
{
    public IReadOnlyList<IAuditVocabularyProvider> Providers { get; } = providers.ToList();

    /// <summary>The words of a record type; an unknown type shows its last part ("Widget").</summary>
    public EntityWords Entity(string? type)
    {
        if (type is not null)
            foreach (var p in Providers)
                if (p.Entities.TryGetValue(type, out var w)) return w;
        return UnknownEntity(type);
    }

    /// <summary>The words of an action; an unknown one is shown split into words ("Something happened").</summary>
    public ActionWords Action(string action)
    {
        foreach (var p in Providers)
            if (p.Actions.TryGetValue(action, out var w)) return w;
        return UnknownAction(action);
    }

    /// <summary>A field's label and kind: per record type first, then by column name.</summary>
    public FieldWords? Field(string? entityType, string column)
    {
        if (entityType is not null)
            foreach (var p in Providers)
                if (p.EntityFields.TryGetValue(entityType + "." + column, out var specific)) return specific;
        foreach (var p in Providers)
            if (p.Fields.TryGetValue(column, out var w)) return w;
        return null;
    }

    /// <summary>Technical columns never shown to readers.</summary>
    public bool IsHidden(string column) => Providers.Any(p => p.Hidden.Contains(column));

    /// <summary>A stored code in words; an unknown code is shown as stored.</summary>
    public string Code(string? code, bool en)
    {
        if (code is null) return "";
        foreach (var p in Providers)
            if (p.Codes.TryGetValue(code, out var w)) return w.In(en);
        return code;
    }

    /// <summary>Whether a code has words (used to translate tokens of a reason such as "Claim · Copy").</summary>
    public bool IsCode(string code) => Providers.Any(p => p.Codes.ContainsKey(code));

    /// <summary>Personal fields per record type of every module (<see cref="ErasedRecords"/>).</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> PersonalFields => _personal ??= Providers.SelectMany(p => p.PersonalFields)
        .GroupBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

    private Dictionary<string, IReadOnlySet<string>>? _personal;

    /// <summary>The first answer of a hook, null when no module has one.</summary>
    public T? First<T>(Func<IAuditVocabularyProvider, T?> hook) where T : class
    {
        foreach (var p in Providers)
            if (hook(p) is { } answer) return answer;
        return null;
    }

    /// <summary>The first answer of a hook returning a value type, null when no module has one.</summary>
    public T? FirstValue<T>(Func<IAuditVocabularyProvider, T?> hook) where T : struct
    {
        foreach (var p in Providers)
            if (hook(p) is { } answer) return answer;
        return null;
    }

    public bool Any(Func<IAuditVocabularyProvider, bool> hook) => Providers.Any(hook);
}
