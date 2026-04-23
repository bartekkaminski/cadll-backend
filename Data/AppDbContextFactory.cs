using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace cadll.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, ".env")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir != null)
            Env.Load(Path.Combine(dir, ".env"));

        var connStr = Environment.GetEnvironmentVariable("DBConnectionString")
            ?? throw new InvalidOperationException(
                "Brak zmiennej DBConnectionString. Dodaj ją do .env lub zmiennych systemowych.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connStr)
            .Options;

        return new AppDbContext(options);
    }
}
