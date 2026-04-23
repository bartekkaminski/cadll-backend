using OpenAI;
using OpenAI.Chat;

namespace cadll.Services;

public class OpenAiService : CodeGeneratorBase, ICodeGeneratorService
{
    private readonly ChatClient _chat;
    private readonly string _model;
    private readonly ILogger<OpenAiService> _logger;

    public OpenAiService(ILogger<OpenAiService> logger)
    {
        _logger = logger;
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "Missing environment variable OPENAI_API_KEY.");

        _model = Environment.GetEnvironmentVariable("AI_MODEL") ?? "gpt-4.1";
        _chat = new OpenAIClient(key).GetChatClient(_model);
        _logger.LogInformation("AI provider: OpenAI | model: {Model}", _model);
    }

    public async Task<CodeResult> GenerateFunctionCodeAsync(string functionName, string prompt, string platform)
    {
        _logger.LogInformation("Generating [{Function}] for {Platform} using {Model}", functionName, platform, _model);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(BuildSystemPrompt(functionName, platform)),
            new UserChatMessage(BuildUserMessage(functionName, prompt))
        };

        var response = await _chat.CompleteChatAsync(messages);
        var calledAt = DateTime.UtcNow;
        var usage = response.Value.Usage;
        _logger.LogInformation(
            "--- TOKENY [GenerateCode|{Function}] in={In} out={Out} | łącznie={Total}",
            functionName, usage.InputTokenCount, usage.OutputTokenCount,
            usage.InputTokenCount + usage.OutputTokenCount);

        var code = FixCommonMistakes(ExtractCodeBlock(response.Value.Content[0].Text));
        return new CodeResult(code, usage.InputTokenCount, usage.OutputTokenCount, _model, calledAt);
    }

    public async Task<CodeResult> FixCodeAsync(string brokenCode, IReadOnlyList<string> errors, string platform)
    {
        var errorList = string.Join("\n", errors.Select((e, i) => $"{i + 1}. {e}"));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(BuildSystemPrompt("FIX", platform) +
                "\n\nYour task now: fix compilation errors in the code below. " +
                "Return ONLY the corrected code in a ```csharp block. No comments, no explanations."),
            new UserChatMessage(
                $"The following C# code has compilation errors:\n\n```csharp\n{brokenCode}\n```\n\n" +
                $"Errors:\n{errorList}\n\n" +
                "Fix all errors. Return only the corrected ```csharp code block.")
        };

        var response = await _chat.CompleteChatAsync(messages);
        var calledAt = DateTime.UtcNow;
        var usage = response.Value.Usage;
        _logger.LogInformation(
            "--- TOKENY [FixCode|—] in={In} out={Out} | łącznie={Total}",
            usage.InputTokenCount, usage.OutputTokenCount,
            usage.InputTokenCount + usage.OutputTokenCount);

        var code = FixCommonMistakes(ExtractCodeBlock(response.Value.Content[0].Text));
        return new CodeResult(code, usage.InputTokenCount, usage.OutputTokenCount, _model, calledAt);
    }
}
