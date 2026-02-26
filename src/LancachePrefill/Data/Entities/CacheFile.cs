using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LancachePrefill.Data.Entities;

[Table("cache_files")]
public class CacheFile
{
    [Key, Column("file_hash")]
    public string FileHash { get; set; } = "";

    [Column("depot_id")]
    public uint DepotId { get; set; }
}
