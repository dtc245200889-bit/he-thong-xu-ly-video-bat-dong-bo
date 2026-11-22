using MediaApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaApi.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobResult> JobResults => Set<JobResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Video>()
            .HasMany(video => video.Jobs)
            .WithOne(job => job.Video)
            .HasForeignKey(job => job.VideoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Job>()
            .HasIndex(job => job.VideoId);

        modelBuilder.Entity<Job>()
            .HasOne(job => job.Result)
            .WithOne(result => result.Job)
            .HasForeignKey<JobResult>(result => result.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobResult>()
            .HasIndex(result => result.JobId)
            .IsUnique();
    }
}
