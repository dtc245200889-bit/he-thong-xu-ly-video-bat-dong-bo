using MediaApi.Models;
using MediaApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediaApi.Controllers;

[ApiController]
[Route("videos")]
public sealed class VideosController(VideoService videoService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Video>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Video>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await videoService.GetAllAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Video), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Video>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var video = await videoService.GetByIdAsync(id, cancellationToken);
        return video is null
            ? NotFound(new { message = $"Video {id} không tồn tại." })
            : Ok(video);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(VideoService.MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = VideoService.MaximumUploadBytes)]
    [ProducesResponseType(typeof(Video), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<Video>> Upload(
        [FromForm(Name = "file")] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Hãy chọn một file video không rỗng ở field 'file'." });
        }

        if (file.Length > VideoService.MaximumUploadBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { message = "Kích thước video tối đa là 100 MB." });
        }

        if (!VideoService.IsSupportedFileName(file.FileName))
        {
            return BadRequest(new
            {
                message = "Định dạng hỗ trợ: .mp4, .mov, .mkv, .avi, .webm."
            });
        }

        await using var fileStream = file.OpenReadStream();
        var video = await videoService.UploadAsync(
            file.FileName,
            fileStream,
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = video.Id }, video);
    }
}
