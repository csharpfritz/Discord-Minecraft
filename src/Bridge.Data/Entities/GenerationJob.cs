namespace Bridge.Data.Entities;

public sealed class GenerationJob
{
    public int Id { get; set; }
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public GenerationJobStatus Status { get; set; } = GenerationJobStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum GenerationJobStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}
