namespace cadll.Services;

public interface ICodeGeneratorService
{
    Task<string> GenerateFunctionCodeAsync(string functionName, string prompt, string platform);
    Task<string> FixCodeAsync(string brokenCode, IReadOnlyList<string> errors, string platform);
}
