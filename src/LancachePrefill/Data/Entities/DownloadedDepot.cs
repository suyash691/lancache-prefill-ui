using System.ComponentModel.DataAnnotations.Schema;

namespace LancachePrefill.Data.Entities;

[Table("downloaded_depots")]
public class DownloadedDepot
{
    [Column("depot_id")]
    public uint DepotId { get; set; }

    [Column("manifest_id")]
    public ulong ManifestId { get; set; }

    [Column("downloaded_at")]
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
}
