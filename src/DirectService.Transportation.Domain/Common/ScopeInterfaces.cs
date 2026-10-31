namespace DirectService.Transportation.Domain.Common;

/// <summary>Loading or unloading site (two separate lists with the same fields, FR-160).</summary>
public interface ISite : IArchivable
{
    string Name { get; set; }
    string? Area { get; set; }
}
