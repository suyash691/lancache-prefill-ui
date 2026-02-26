using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LancachePrefill.Data.Entities;

[Table("scan_results")]
public class ScanResultEntity
{
    [Key, Column("app_id")]
    public uint AppId { get; set; }

    [Column("app_name")]
    public string AppName { get; set; } = "";

    [Column("cached")]
    public bool Cached { get; set; }

    [Column("error")]
    public string? Error { get; set; }

    [Column("scanned_at")]
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}
