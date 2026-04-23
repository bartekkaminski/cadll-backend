namespace cadll.Data.Entities;

public class GenerationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string UserIp { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? FinalCode { get; set; }
    public int TotalAiCalls { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }

    public List<AiApiCall> AiApiCalls { get; set; } = [];
}
