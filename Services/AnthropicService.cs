using Anthropic;
using Anthropic.Models.Messages;

namespace cadll.Services;

public class AnthropicService : CodeGeneratorBase, ICodeGeneratorService
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly ILogger<AnthropicService> _logger;

    public AnthropicService(ILogger<AnthropicService> logger)
    {
        _logger = logger;
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Missing environment variable ANTHROPIC_API_KEY.");

        _client = new AnthropicClient { ApiKey = key };
        _model = Environment.GetEnvironmentVariable("AI_MODEL") ?? "claude-sonnet-4-6";
        _logger.LogInformation("AI provider: Anthropic | model: {Model}", _model);
    }

    public async Task<CodeResult> GenerateFunctionCodeAsync(string functionName, string prompt, string platform)
    {
        _logger.LogInformation("Generating [{Function}] for {Platform} using {Model}", functionName, platform, _model);
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            MaxTokens = 8192,
            Model = _model,
            System = BuildSystemPrompt(functionName, platform),
            Messages =
            [
                new() { Role = Role.User, Content = BuildUserMessage(functionName, prompt) }
            ]
        });

        var calledAt = DateTime.UtcNow;
        LogUsage("GenerateCode", functionName, response);
        var code = FixCommonMistakes(ExtractCodeBlock(ExtractText(response)));
        return new CodeResult(code, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens, _model, calledAt);
    }

    public async Task<CodeResult> FixCodeAsync(string brokenCode, IReadOnlyList<string> errors, string platform)
    {
        var errorList = string.Join("\n", errors.Select((e, i) => $"{i + 1}. {e}"));

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            MaxTokens = 8192,
            Model = _model,
            System = BuildSystemPrompt("FIX", platform) +
                     "\n\nYour task now: fix compilation errors in the code below. " +
                     "Return ONLY the corrected code in a ```csharp block. No comments, no explanations.",
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = $"The following C# code has compilation errors:\n\n```csharp\n{brokenCode}\n```\n\n" +
                              $"Errors:\n{errorList}\n\n" +
                              "Fix all errors. Return only the corrected ```csharp code block."
                }
            ]
        });

        var calledAt = DateTime.UtcNow;
        LogUsage("FixCode", "—", response);
        var code = FixCommonMistakes(ExtractCodeBlock(ExtractText(response)));
        return new CodeResult(code, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens, _model, calledAt);
    }

    private void LogUsage(string operation, string context, Message response)
    {
        var u = response.Usage;
        _logger.LogInformation(
            "--- TOKENY [{Op}|{Ctx}] in={In} out={Out} | łącznie={Total}",
            operation, context,
            u.InputTokens, u.OutputTokens,
            u.InputTokens + u.OutputTokens);
    }

    private static string ExtractText(Message response)
    {
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
                return textBlock.Text;
        }
        return string.Empty;
    }
}
