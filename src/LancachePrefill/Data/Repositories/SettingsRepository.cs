using LancachePrefill.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LancachePrefill.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<PrefillDbContext> _factory;
    public SettingsRepository(IDbContextFactory<PrefillDbContext> factory) => _factory = factory;

    public string? GetSetting(string key)
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.Settings.Find(key)?.Value;
    }

    public void SetSetting(string key, string value)
    {
        using var ctx = _factory.CreateDbContext();
        var existing = ctx.Settings.Find(key);
        if (existing != null) existing.Value = value;
        else ctx.Settings.Add(new Setting { Key = key, Value = value });
        ctx.SaveChanges();
    }

    public Dictionary<string, string> GetAllSettings()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.Settings.ToDictionary(s => s.Key, s => s.Value);
    }
}
