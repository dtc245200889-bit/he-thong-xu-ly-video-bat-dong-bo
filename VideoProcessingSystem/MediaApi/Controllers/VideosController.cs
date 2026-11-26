using MediaApi.Models;
using MediaApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediaApi.Controllers;

[ApiController]
[Route("videos")]
public sealed class VideosController(VideoService videoService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<VideoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VideoDto>>> GetAll(CancellationToken cancellationToken)
    {
        var videos = await videoService.GetAllAsync(cancellationToken);
        return Ok(videos.Select(VideoDto.From).ToArray());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(VideoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VideoDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var video = await videoService.GetByIdAsync(id, cancellationToken);
        return video is null
            ? NotFound(new ApiError("VIDEO_NOT_FOUND", $"Video {id} không tồn tại."))
            : Ok(VideoDto.From(video));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(VideoService.MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = VideoService.MaximumUploadBytes)]
    [ProducesResponseType(typeof(VideoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<VideoDto>> Upload(
        [FromForm(Name = "file")] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ApiError(
                "INVALID_VIDEO_FILE",
                "Hãy chọn một file video không rỗng ở field 'file'."));
        }

        if (file.Length > VideoService.MaximumUploadBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new ApiError("VIDEO_TOO_LARGE", "Kích thước video tối đa là 100 MB."));
        }

        if (!VideoService.IsSupportedFileName(file.FileName))
        {
            return BadRequest(new ApiError(
                "UNSUPPORTED_VIDEO_FORMAT",
                "Định dạng hỗ trợ: .mp4, .mov, .mkv, .avi, .webm."));
        }

        await using var fileStream = file.OpenReadStream();
        var video = await videoService.UploadAsync(
            file.FileName,
            fileStream,
            cancellationToken);

        var videoDto = VideoDto.From(video);
        return CreatedAtAction(nameof(GetById), new { id = video.Id }, videoDto);
    }
}
