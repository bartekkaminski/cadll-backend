namespace cadll.Models;

public enum JobStatus { Pending, Done, Error }

public class JobResult
{
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public string Phase { get; set; } = "generating";
    public byte[]? ZipBytes { get; set; }
    public IReadOnlyList<string>? Errors { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
}
