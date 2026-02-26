using LancachePrefill.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LancachePrefill.Data;

public class PrefillDbContext : DbContext
{
    public DbSet<SelectedApp> SelectedApps => Set<SelectedApp>();
    public DbSet<DownloadedDepot> DownloadedDepots => Set<DownloadedDepot>();
    public DbSet<DepotAppMapping> DepotAppMappings => Set<DepotAppMapping>();
    public DbSet<CacheFile> CacheFiles => Set<CacheFile>();
    public DbSet<ScanResultEntity> ScanResults => Set<ScanResultEntity>();
    public DbSet<Setting> Settings => Set<Setting>();

    public PrefillDbContext(DbContextOptions<PrefillDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<DownloadedDepot>()
            .HasKey(d => new { d.DepotId, d.ManifestId });

        m.Entity<DownloadedDepot>()
            .Property(d => d.DownloadedAt).HasDefaultValueSql("datetime('now')");

        m.Entity<SelectedApp>()
            .Property(a => a.AddedAt).HasDefaultValueSql("datetime('now')");

        m.Entity<SelectedApp>()
            .Property(a => a.Status).HasDefaultValue("active");

        m.Entity<SelectedApp>()
            .HasIndex(a => a.Status)
            .HasDatabaseName("IX_selected_apps_status");

        m.Entity<ScanResultEntity>()
            .Property(r => r.ScannedAt).HasDefaultValueSql("datetime('now')");

        m.Entity<CacheFile>()
            .HasIndex(c => c.DepotId);

        // SQLite stores uint/ulong as INTEGER
        foreach (var entity in m.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(uint))
                    prop.SetValueConverter(new ValueConverter<uint, long>(v => (long)v, v => (uint)v));
                else if (prop.ClrType == typeof(ulong))
                    prop.SetValueConverter(new ValueConverter<ulong, long>(v => (long)v, v => (ulong)v));
            }
    }
}
