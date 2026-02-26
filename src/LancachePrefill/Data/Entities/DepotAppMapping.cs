using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LancachePrefill.Data.Entities;

[Table("depot_app_map")]
public class DepotAppMapping
{
    [Key, Column("depot_id")]
    public uint DepotId { get; set; }

    [Column("app_id")]
    public uint AppId { get; set; }

    [Column("app_name")]
    public string? AppName { get; set; }
}
