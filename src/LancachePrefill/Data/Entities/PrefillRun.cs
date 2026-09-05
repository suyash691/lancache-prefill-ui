using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LancachePrefill.Data.Entities;

/// <summary>One completed (or cancelled) prefill run — the job history record.</summary>
[Table("prefill_runs")]
public class PrefillRun
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    [Column("finished_at")]
    public DateTime FinishedAt { get; set; }

    /// <summary>manual | scheduled | queued</summary>
    [Column("run_trigger")]
    public string Trigger { get; set; } = "manual";

    /// <summary>done | cancelled</summary>
    [Column("status")]
    public string Status { get; set; } = "done";

    [Column("apps_cached")]
    public int AppsCached { get; set; }

    [Column("apps_partial")]
    public int AppsPartial { get; set; }

    [Column("apps_skipped")]
    public int AppsSkipped { get; set; }

    [Column("apps_failed")]
    public int AppsFailed { get; set; }

    [Column("bytes")]
    public long Bytes { get; set; }

    /// <summary>Compact per-app results: [{appId,name,status,bytes},...]</summary>
    [Column("results_json")]
    public string? ResultsJson { get; set; }
}
