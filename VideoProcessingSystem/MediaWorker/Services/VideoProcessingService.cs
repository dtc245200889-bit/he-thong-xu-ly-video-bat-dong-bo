using System.Text.Json;
using MediaWorker.Models;
using Microsoft.Data.Sqlite;

namespace MediaWorker.Services;

public sealed class VideoProcessingService
{
    private readonly string _connectionString;
    private readonly string _outputDirectory;
    private readonly ILogger<VideoProcessingService> _logger;

    public VideoProcessingService(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<VideoProcessingService> logger)
    {
        var storageRoot = Path.GetFullPath(
            configuration["Storage:RootPath"] ?? "../storage",
            environment.ContentRootPath);
        var databasePath = Path.Combine(storageRoot, "media.db");

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                "SQLite database was not found. Start MediaApi once to initialize it.",
                databasePath);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
        _outputDirectory = Path.Combine(storageRoot, "outputs");
        _logger = logger;

        Directory.CreateDirectory(_outputDirectory);
    }

    public async Task ProcessAsync(
        VideoJobMessage message,
        CancellationToken cancellationToken)
    {
        var jobContext = await LoadJobAsync(message.JobId, cancellationToken);
        if (jobContext is null)
        {
            _logger.LogWarning("Job {JobId} was not found; acknowledging message", message.JobId);
            return;
        }

        if (jobContext.Status is "Completed" or "Failed")
        {
            _logger.LogInformation("Job {JobId} is already terminal", message.JobId);
            return;
        }

        if (jobContext.VideoId != message.VideoId)
        {
            await MarkFailedAsync(
                message.JobId,
                "The message video ID does not match the database job.",
                cancellationToken);
            return;
        }

        if (jobContext.StoragePath is null || !File.Exists(jobContext.StoragePath))
        {
            await MarkFailedAsync(
                message.JobId,
                "The uploaded video file is missing.",
                cancellationToken);
            return;
        }

        if (!string.Equals(
            message.TaskType,
            "SimulatedTranscode",
            StringComparison.Ordinal))
        {
            await MarkFailedAsync(
                message.JobId,
                $"Unsupported task type: {message.TaskType}.",
                cancellationToken);
            return;
        }

        await UpdateProgressAsync(message.JobId, 10, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        await UpdateProgressAsync(message.JobId, 35, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        await UpdateProgressAsync(message.JobId, 70, cancellationToken);

        var outputPath = Path.Combine(_outputDirectory, $"{message.JobId:N}.json");
        var metadata = new
        {
            simulationOnly = true,
            taskType = message.TaskType,
            sourceFileName = jobContext.FileName,
            sourceFileSizeBytes = new FileInfo(jobContext.StoragePath).Length,
            note = "Simulated processing only; no video transcoding was performed.",
            completedAtUtc = DateTime.UtcNow
        };
        var metadataJson = JsonSerializer.Serialize(
            metadata,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });

        await File.WriteAllTextAsync(outputPath, metadataJson, cancellationToken);
        await SaveResultAsync(
            message.JobId,
            outputPath,
            metadataJson,
            cancellationToken);

        _logger.LogInformation("Job {JobId} completed at {OutputPath}", message.JobId, outputPath);
    }

    public async Task MarkFailedAsync(
        Guid jobId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Jobs
            SET Status = 'Failed',
                CompletedAtUtc = $completedAt,
                ErrorMessage = $errorMessage
            WHERE Id = $jobId;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        command.Parameters.AddWithValue("$completedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("$errorMessage", errorMessage);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<JobContext?> LoadJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Jobs.VideoId, Jobs.TaskType, Jobs.Status,
                   Videos.FileName, Videos.StoragePath
            FROM Jobs
            LEFT JOIN Videos ON Videos.Id = Jobs.VideoId
            WHERE Jobs.Id = $jobId;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new JobContext(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private async Task UpdateProgressAsync(
        Guid jobId,
        int progress,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Jobs
            SET Status = 'Processing', Progress = $progress
            WHERE Id = $jobId;
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        command.Parameters.AddWithValue("$progress", progress);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Job {JobId} progress: {Progress}%", jobId, progress);
    }

    private async Task SaveResultAsync(
        Guid jobId,
        string outputPath,
        string metadataJson,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using (var resultCommand = connection.CreateCommand())
        {
            resultCommand.Transaction = transaction;
            resultCommand.CommandText = """
                INSERT INTO JobResults (Id, JobId, OutputFilePath, MetadataJson, CreatedAtUtc)
                VALUES ($id, $jobId, $outputPath, $metadataJson, $createdAt)
                ON CONFLICT(JobId) DO UPDATE SET
                    OutputFilePath = excluded.OutputFilePath,
                    MetadataJson = excluded.MetadataJson,
                    CreatedAtUtc = excluded.CreatedAtUtc;
                """;
            resultCommand.Parameters.AddWithValue("$id", Guid.NewGuid());
            resultCommand.Parameters.AddWithValue("$jobId", jobId);
            resultCommand.Parameters.AddWithValue("$outputPath", outputPath);
            resultCommand.Parameters.AddWithValue("$metadataJson", metadataJson);
            resultCommand.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);
            await resultCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var jobCommand = connection.CreateCommand())
        {
            jobCommand.Transaction = transaction;
            jobCommand.CommandText = """
                UPDATE Jobs
                SET Status = 'Completed',
                    Progress = 100,
                    CompletedAtUtc = $completedAt,
                    ErrorMessage = NULL
                WHERE Id = $jobId;
                """;
            jobCommand.Parameters.AddWithValue("$jobId", jobId);
            jobCommand.Parameters.AddWithValue("$completedAt", DateTime.UtcNow);
            await jobCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private sealed record JobContext(
        Guid VideoId,
        string TaskType,
        string Status,
        string? FileName,
        string? StoragePath);
}
