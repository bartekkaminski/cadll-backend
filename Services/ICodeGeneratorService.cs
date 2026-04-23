namespace cadll.Services;

public interface ICodeGeneratorService
{
    Task<string> GenerateFunctionCodeAsync(string functionName, string prompt);
    Task<string> FixCodeAsync(string brokenCode, IReadOnlyList<string> errors);
}
