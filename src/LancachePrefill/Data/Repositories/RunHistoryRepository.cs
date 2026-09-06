using LancachePrefill.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LancachePrefill.Data.Repositories;

public interface IRunHistoryRepository
{
    void AddRun(PrefillRun run);
    List<PrefillRun> GetRuns(int limit = 50);
}

public class RunHistoryRepository : IRunHistoryRepository
{
    private const int MaxRetainedRuns = 100;

    private readonly IDbContextFactory<PrefillDbContext> _factory;
    public RunHistoryRepository(IDbContextFactory<PrefillDbContext> factory) => _factory = factory;

    public void AddRun(PrefillRun run)
    {
        using var ctx = _factory.CreateDbContext();
        ctx.PrefillRuns.Add(run);
        ctx.SaveChanges();

        // Prune: keep only the newest MaxRetainedRuns
        var stale = ctx.PrefillRuns.OrderByDescending(r => r.StartedAt)
            .Skip(MaxRetainedRuns).ToList();
        if (stale.Count > 0)
        {
            ctx.PrefillRuns.RemoveRange(stale);
            ctx.SaveChanges();
        }
    }

    public List<PrefillRun> GetRuns(int limit = 50)
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.PrefillRuns.OrderByDescending(r => r.StartedAt).Take(limit).ToList();
    }
}
