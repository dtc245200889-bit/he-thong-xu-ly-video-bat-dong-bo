using MediaApi.Messaging;
using MediaApi.Models;
using MediaApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediaApi.Controllers;

[ApiController]
public sealed class JobsController(
    JobService jobService,
    RabbitMqPublisher publisher,
    ILogger<JobsController> logger) : ControllerBase
{
    [HttpPost("videos/{videoId:guid}/jobs")]
    [ProducesResponseType(typeof(JobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<JobDto>> Create(
        Guid videoId,
        CreateJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!JobService.IsSupportedTaskType(request.TaskType))
        {
            return BadRequest(new ApiError(
                "UNSUPPORTED_TASK_TYPE",
                $"TaskType phải là '{JobService.SimulatedTranscodeTask}'."));
        }

        var job = await jobService.CreateAsync(
            videoId,
            cancellationToken);

        if (job is null)
        {
            return NotFound(new ApiError("VIDEO_NOT_FOUND", $"Video {videoId} không tồn tại."));
        }

        try
        {
            publisher.Publish(new VideoJobMessage(job.Id, videoId, job.TaskType));
        }
        catch (RabbitMqPublishException ex)
        {
            logger.LogError(ex, "Failed to publish job {JobId}", job.Id);
            await jobService.MarkFailedAsync(
                job.Id,
                "Không thể gửi job vào RabbitMQ.",
                cancellationToken);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiError("RABBITMQ_UNAVAILABLE", "RabbitMQ hiện không khả dụng."));
        }

        return AcceptedAtAction(nameof(GetById), new { id = job.Id }, JobDto.From(job));
    }

    [HttpGet("jobs/{id:guid}")]
    [ProducesResponseType(typeof(JobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var job = await jobService.GetByIdAsync(id, cancellationToken);
        return job is null
            ? NotFound(new ApiError("JOB_NOT_FOUND", $"Job {id} không tồn tại."))
            : Ok(JobDto.From(job));
    }
}
