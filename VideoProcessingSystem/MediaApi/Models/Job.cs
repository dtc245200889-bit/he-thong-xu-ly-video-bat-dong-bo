using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MediaApi.Models;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VideoId { get; set; }

    [JsonIgnore]
    public Video Video { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string TaskType { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public int Progress { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public JobResult? Result { get; set; }
}
