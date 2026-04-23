namespace cadll.Models;

public record GenerateRequest(string FunctionName, string Prompt, string Platform = "zwcad");
