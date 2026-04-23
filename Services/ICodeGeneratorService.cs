namespace cadll.Services;

public record CodeResult(string Code, int InputTokens, int OutputTokens, string AiModel, DateTime CalledAt);

public interface ICodeGeneratorService
{
    Task<CodeResult> GenerateFunctionCodeAsync(string functionName, string prompt, string platform);
    Task<CodeResult> FixCodeAsync(string brokenCode, IReadOnlyList<string> errors, string platform);
}
