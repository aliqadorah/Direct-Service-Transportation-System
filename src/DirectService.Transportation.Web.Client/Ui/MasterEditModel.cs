namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>Editable values of a simple master record (materials, sites, ownership types): name, the one extra field, row version.</summary>
public sealed class MasterEditModel
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Extra { get; set; }
    public string? RowVersion { get; set; }
    /// <summary>Idempotent create (DT-49): the same key on a retry returns the record already created.</summary>
    public string ClientId { get; set; } = Guid.NewGuid().ToString();
}
