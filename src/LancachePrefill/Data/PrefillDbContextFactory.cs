using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LancachePrefill.Data;

/// <summary>
/// Design-time factory for EF Core CLI tools (dotnet ef migrations add, etc.)
/// </summary>
public class PrefillDbContextFactory : IDesignTimeDbContextFactory<PrefillDbContext>
{
    public PrefillDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PrefillDbContext>()
            .UseSqlite("Data Source=lancache-prefill.db")
            .Options;
        return new PrefillDbContext(options);
    }
}
