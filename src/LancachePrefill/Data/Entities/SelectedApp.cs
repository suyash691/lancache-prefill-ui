using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LancachePrefill.Data.Entities;

[Table("selected_apps")]
public class SelectedApp
{
    [Key, Column("app_id")]
    public uint AppId { get; set; }

    [Column("added_at")]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// App status: active, partial, evicted, pending
    /// </summary>
    [Column("status")]
    public string Status { get; set; } = "active";
}