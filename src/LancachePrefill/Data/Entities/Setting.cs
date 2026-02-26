using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LancachePrefill.Data.Entities;

[Table("settings")]
public class Setting
{
    [Key, Column("key")]
    public string Key { get; set; } = "";

    [Column("value")]
    public string Value { get; set; } = "";
}
