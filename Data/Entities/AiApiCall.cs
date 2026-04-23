namespace cadll.Data.Entities;

public class AiApiCall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string AiModel { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? ResponseCode { get; set; }
    public DateTime CalledAt { get; set; } = DateTime.UtcNow;

    public GenerationJob Job { get; set; } = null!;
}
