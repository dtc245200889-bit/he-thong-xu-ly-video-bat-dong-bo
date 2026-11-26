using System.ComponentModel.DataAnnotations;

namespace MediaApi.Models;

public sealed record ApiError(string Code, string Message);

public sealed record CreateJobRequest([param: Required(ErrorMessage = "TaskType là bắt buộc.")] string? TaskType);

public sealed record VideoDto(
    Guid Id,
    string FileName,
    DateTime CreatedAtUtc,
    IReadOnlyList<JobDto> Jobs)
{
    public static VideoDto From(Video video) =>
        new(
            video.Id,
            video.FileName,
            video.CreatedAtUtc,
            video.Jobs.Select(JobDto.From).ToArray());
}

public sealed record JobDto(
    Guid Id,
    Guid VideoId,
    string TaskType,
    string Status,
    int Progress,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage,
    JobResultDto? Result)
{
    public static JobDto From(Job job) =>
        new(
            job.Id,
            job.VideoId,
            job.TaskType,
            job.Status,
            job.Progress,
            job.CreatedAtUtc,
            job.CompletedAtUtc,
            job.ErrorMessage,
            job.Result is null ? null : JobResultDto.From(job.Result));
}

public sealed record JobResultDto(
    Guid Id,
    string MetadataJson,
    DateTime CreatedAtUtc)
{
    public static JobResultDto From(JobResult result) =>
        new(result.Id, result.MetadataJson, result.CreatedAtUtc);
}
