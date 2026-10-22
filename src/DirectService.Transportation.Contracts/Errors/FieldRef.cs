namespace DirectService.Transportation.Contracts.Errors;

/// <summary>
/// A message argument that names a request field or path ("entryIds", "lines[2].accountId") or a code value with a label (party types):
/// <see cref="ErrorCatalog.Format"/> shows it by its label from <see cref="FieldLabels"/> (DT-241). Only these are labelled — a plain string
/// argument (a customer, account or template name, a number) is always shown as it is, even when it equals a field name (DT-245).
/// </summary>
public readonly record struct FieldRef(string Name)
{
    public override string ToString() => Name;
}
