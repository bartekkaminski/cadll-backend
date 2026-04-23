namespace cadll.Services;

public abstract class CodeGeneratorBase
{

    protected static string BuildSystemPrompt(string functionName, string platform) =>
        SystemPrompts.GetPrompt(platform, functionName);

    protected static string BuildUserMessage(string functionName, string prompt) =>
        $"Function name: {functionName}\n\nDescription:\n{prompt}";

    protected static string ExtractCodeBlock(string response)
    {
        var fence = "```csharp";
        var start = response.IndexOf(fence, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start = response.IndexOf('\n', start) + 1;
            var end = response.IndexOf("```", start);
            if (end >= 0)
                return response[start..end].Trim();
        }

        start = response.IndexOf("```");
        if (start >= 0)
        {
            start = response.IndexOf('\n', start) + 1;
            var end = response.IndexOf("```", start);
            if (end >= 0)
                return response[start..end].Trim();
        }

        return response.Trim();
    }

    protected static string FixCommonMistakes(string code)
    {
        var methodsWithStringComparison = new[]
        {
            "IndexOf", "LastIndexOf", "StartsWith", "EndsWith",
            "Contains", "Replace", "Compare", "Equals"
        };

        foreach (var method in methodsWithStringComparison)
        {
            code = System.Text.RegularExpressions.Regex.Replace(
                code,
                $@"(\.{method}\s*\([^)]*),\s*StringComparer\.(\w+)\)",
                $"$1, StringComparison.$2)");
        }

        code = System.Text.RegularExpressions.Regex.Replace(
            code,
            @"(string\s*\.\s*(?:Compare|Equals)\s*\([^)]*),\s*StringComparer\.(\w+)\)",
            "$1, StringComparison.$2)");

        code = System.Text.RegularExpressions.Regex.Replace(
            code,
            @"(new\s+(?:Sorted)?Dictionary\s*<\s*\([^)]+\)[^>]*>\s*\()\s*StringComparer\.\w+\s*\)",
            "$1)");

        code = System.Text.RegularExpressions.Regex.Replace(
            code,
            @"([A-Za-z]\w*[A-Z][a-z]{1,4})\s+([a-z]\w*)\s*(\()",
            "$1$2$3");

        if (code.Contains("System.Text.RegularExpressions") || code.Contains("using System.Text.RegularExpressions"))
        {
            code = System.Text.RegularExpressions.Regex.Replace(
                code,
                @"\bGroup\b(\s+\w+\s*=)",
                "System.Text.RegularExpressions.Group$1");
        }

        code = System.Text.RegularExpressions.Regex.Replace(
            code,
            @"^( *)(//[^\n]+?)((?:if|else if|foreach|while|for)\s*[\(\{])",
            "$1$2\n$1$3",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        return code;
    }
}
