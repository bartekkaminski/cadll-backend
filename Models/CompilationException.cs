namespace cadll.Models;

public class CompilationException(IReadOnlyList<string> errors)
    : Exception("Compilation failed")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
