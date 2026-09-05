using LancachePrefill.Data;
using LancachePrefill.Data.Entities;
using LancachePrefill.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LancachePrefill.Tests;

public class RunHistoryRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly RunHistoryRepository _repo;

    private class Factory : IDbContextFactory<PrefillDbContext>
    {
        private readonly DbContextOptions<PrefillDbContext> _options;
        public Factory(DbContextOptions<PrefillDbContext> options) => _options = options;
        public PrefillDbContext CreateDbContext() => new(_options);
    }

    public RunHistoryRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lancache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
        var options = new DbContextOptionsBuilder<PrefillDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "test.db")}").Options;
        var factory = new Factory(options);
        using (var ctx = factory.CreateDbContext()) ctx.Database.Migrate();
        _repo = new RunHistoryRepository(factory);
    }

    private static PrefillRun Run(DateTime started, string trigger = "manual") => new()
    {
        StartedAt = started,
        FinishedAt = started.AddMinutes(5),
        Trigger = trigger,
        Status = "done",
        AppsCached = 2,
        AppsSkipped = 1,
        Bytes = 123_456,
        ResultsJson = """[{"appId":730,"name":"CS2","status":"cached","bytes":123456}]"""
    };

    [Fact]
    public void AddAndGet_RoundTrips_NewestFirst()
    {
        var t0 = DateTime.UtcNow.AddHours(-2);
        _repo.AddRun(Run(t0));
        _repo.AddRun(Run(t0.AddHours(1), trigger: "scheduled"));

        var runs = _repo.GetRuns();
        Assert.Equal(2, runs.Count);
        Assert.Equal("scheduled", runs[0].Trigger); // newest first
        Assert.Equal(2, runs[0].AppsCached);
        Assert.Contains("CS2", runs[0].ResultsJson);
    }

    [Fact]
    public void GetRuns_RespectsLimit()
    {
        var t0 = DateTime.UtcNow.AddHours(-5);
        for (int i = 0; i < 5; i++) _repo.AddRun(Run(t0.AddMinutes(i)));
        Assert.Equal(3, _repo.GetRuns(3).Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
