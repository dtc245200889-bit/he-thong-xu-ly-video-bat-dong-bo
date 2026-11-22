using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MediaApi.Models;

public sealed class JobResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }

    [JsonIgnore]
    public Job Job { get; set; } = null!;

    [Required]
    [MaxLength(2048)]
    public string OutputFilePath { get; set; } = string.Empty;

    [Required]
    public string MetadataJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
