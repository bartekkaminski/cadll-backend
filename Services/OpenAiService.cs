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

    public async Task<string> GenerateFunctionCodeAsync(string functionName, string prompt, string platform)
    {
        _logger.LogInformation("Generating [{Function}] for {Platform} using {Model}", functionName, platform, _model);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(BuildSystemPrompt(functionName, platform)),
            new UserChatMessage(BuildUserMessage(functionName, prompt))
        };

        var response = await _chat.CompleteChatAsync(messages);
        var text = response.Value.Content[0].Text;
        return FixCommonMistakes(ExtractCodeBlock(text));
    }

    public async Task<string> FixCodeAsync(string brokenCode, IReadOnlyList<string> errors)
    {
        var errorList = string.Join("\n", errors.Select((e, i) => $"{i + 1}. {e}"));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a C# expert. Fix the compilation errors in the provided code. " +
                "Return ONLY the corrected code in a ```csharp block. No comments, no explanations."),
            new UserChatMessage(
                $"The following C# code has compilation errors:\n\n```csharp\n{brokenCode}\n```\n\n" +
                $"Errors:\n{errorList}\n\n" +
                "Fix all errors. Return only the corrected ```csharp code block.")
        };

        var response = await _chat.CompleteChatAsync(messages);
        var text = response.Value.Content[0].Text;
        return FixCommonMistakes(ExtractCodeBlock(text));
    }
}
