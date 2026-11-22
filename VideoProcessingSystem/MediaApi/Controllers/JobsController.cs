using System.ComponentModel.DataAnnotations;
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
    [ProducesResponseType(typeof(Job), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Job>> Create(
        Guid videoId,
        CreateJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!JobService.IsSupportedTaskType(request.TaskType))
        {
            return BadRequest(new
            {
                message = $"TaskType phải là '{JobService.SimulatedTranscodeTask}'."
            });
        }

        var job = await jobService.CreateAsync(
            videoId,
            cancellationToken);

        if (job is null)
        {
            return NotFound(new { message = $"Video {videoId} không tồn tại." });
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

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "RabbitMQ hiện không khả dụng.",
                jobId = job.Id
            });
        }

        return AcceptedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpGet("jobs/{id:guid}")]
    [ProducesResponseType(typeof(Job), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Job>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var job = await jobService.GetByIdAsync(id, cancellationToken);
        return job is null
            ? NotFound(new { message = $"Job {id} không tồn tại." })
            : Ok(job);
    }
}

public sealed record CreateJobRequest([Required] string TaskType);
