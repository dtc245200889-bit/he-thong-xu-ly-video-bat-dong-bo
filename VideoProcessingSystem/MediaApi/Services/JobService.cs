using MediaApi.Data;
using MediaApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaApi.Services;

public sealed class JobService(AppDbContext database)
{
    public const string SimulatedTranscodeTask = "SimulatedTranscode";

    public static bool IsSupportedTaskType(string? taskType) =>
        string.Equals(
            taskType?.Trim(),
            SimulatedTranscodeTask,
            StringComparison.OrdinalIgnoreCase);

    public async Task<Job?> CreateAsync(
        Guid videoId,
        CancellationToken cancellationToken)
    {
        var videoExists = await database.Videos
            .AnyAsync(video => video.Id == videoId, cancellationToken);

        if (!videoExists)
        {
            return null;
        }

        var job = new Job
        {
            VideoId = videoId,
            TaskType = SimulatedTranscodeTask,
            Status = "Pending",
            Progress = 0
        };

        database.Jobs.Add(job);
        await database.SaveChangesAsync(cancellationToken);
        return job;
    }

    public Task<Job?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return database.Jobs
            .Include(job => job.Result)
            .FirstOrDefaultAsync(job => job.Id == id, cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid id,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var job = await database.Jobs.FirstOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);

        if (job is null)
        {
            return;
        }

        job.Status = "Failed";
        job.CompletedAtUtc = DateTime.UtcNow;
        job.ErrorMessage = errorMessage;
        await database.SaveChangesAsync(cancellationToken);
    }
}
