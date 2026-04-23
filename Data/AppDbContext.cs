using cadll.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace cadll.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GenerationJob> GenerationJobs => Set<GenerationJob>();
    public DbSet<AiApiCall> AiApiCalls => Set<AiApiCall>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenerationJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserIp).HasMaxLength(64);
            e.Property(x => x.FunctionName).HasMaxLength(64);
            e.Property(x => x.Platform).HasMaxLength(32);
            e.Property(x => x.Outcome).HasMaxLength(32);
        });

        modelBuilder.Entity<AiApiCall>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Operation).HasMaxLength(32);
            e.Property(x => x.AiModel).HasMaxLength(64);
            e.HasOne(x => x.Job)
             .WithMany(j => j.AiApiCalls)
             .HasForeignKey(x => x.JobId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
