namespace MediaWorker.Models;

public sealed record VideoJobMessage(Guid JobId, Guid VideoId, string TaskType);
