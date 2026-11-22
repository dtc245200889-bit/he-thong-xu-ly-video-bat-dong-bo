using MediaApi.Data;
using MediaApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaApi.Services;

public sealed class VideoService(
    AppDbContext database,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    public const long MaximumUploadBytes = 100L * 1024 * 1024;
    private static readonly HashSet<string> SupportedExtensions = new(
        [".mp4", ".mov", ".mkv", ".avi", ".webm"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<Video>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await database.Videos
            .Include(video => video.Jobs)
            .ThenInclude(job => job.Result)
            .OrderByDescending(video => video.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<Video?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return database.Videos
            .Include(video => video.Jobs)
            .ThenInclude(job => job.Result)
            .FirstOrDefaultAsync(video => video.Id == id, cancellationToken);
    }

    public static bool IsSupportedFileName(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName));

    public async Task<Video> UploadAsync(
        string originalFileName,
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(originalFileName).Trim();
        var id = Guid.NewGuid();
        var extension = Path.GetExtension(fileName);
        var storageRoot = Path.GetFullPath(
            configuration["Storage:RootPath"] ?? "../storage",
            environment.ContentRootPath);

        var storagePath = Path.Combine(
            storageRoot,
            "uploads",
            $"{id:N}{extension.ToLowerInvariant()}");

        Directory.CreateDirectory(Path.GetDirectoryName(storagePath)!);

        try
        {
            await using (var destination = new FileStream(
                storagePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await fileStream.CopyToAsync(destination, cancellationToken);
            }

            var video = new Video
            {
                Id = id,
                FileName = fileName,
                StoragePath = storagePath
            };

            database.Videos.Add(video);
            await database.SaveChangesAsync(cancellationToken);
            return video;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or DbUpdateException)
        {
            if (File.Exists(storagePath))
            {
                File.Delete(storagePath);
            }

            throw;
        }
    }
}
