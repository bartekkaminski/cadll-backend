using cadll.Models;
using cadll.Services;
using Microsoft.AspNetCore.Mvc;

namespace cadll.Controllers;

[ApiController]
public class GenerateController(
    OpenAiService openAi,
    CompilerService compiler,
    ILogger<GenerateController> logger) : ControllerBase
{
    private const int MaxRetries = 2;

    [HttpPost("api/generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FunctionName))
            return BadRequest(new { error = "FunctionName jest wymagana." });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt jest wymagany." });

        logger.LogInformation(">>> Nowe żądanie: funkcja={Name}", request.FunctionName);

        string? code = null;
        try
        {
            code = await openAi.GenerateFunctionCodeAsync(request.FunctionName, request.Prompt);

            logger.LogInformation(
                "=== WYGENEROWANY KOD [{Name}] ===\n{Code}\n=== KONIEC KODU ===",
                request.FunctionName, code);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var dll = await compiler.CompileAsync(code, request.FunctionName);
                    logger.LogInformation("<<< Sukces: {Name}.dll ({Size} bajtów) — próba {Attempt}",
                        request.FunctionName, dll.Length, attempt);
                    return File(dll, "application/octet-stream", $"{request.FunctionName}.dll");
                }
                catch (CompilationException ex) when (attempt < MaxRetries)
                {
                    logger.LogWarning(
                        "Próba {Attempt}/{Max} nieudana [{Name}]. Błędy:\n{Errors}\nPonawiam z GPT...",
                        attempt, MaxRetries, request.FunctionName,
                        string.Join("\n", ex.Errors));

                    code = await openAi.FixCodeAsync(code, ex.Errors);

                    logger.LogInformation(
                        "=== POPRAWIONY KOD [{Name}] (próba {Next}) ===\n{Code}\n=== KONIEC ===",
                        request.FunctionName, attempt + 1, code);
                }
            }

            // Ostatnia próba — pozwól wyjątkowi się propagować
            var finalDll = await compiler.CompileAsync(code, request.FunctionName);
            logger.LogInformation("<<< Sukces: {Name}.dll ({Size} bajtów) — ostatnia próba",
                request.FunctionName, finalDll.Length);
            return File(finalDll, "application/octet-stream", $"{request.FunctionName}.dll");
        }
        catch (CompilationException ex)
        {
            logger.LogWarning(
                "<<< Błąd kompilacji [{Name}] po {Max} próbach:\n{Errors}\n\nKod:\n{Code}",
                request.FunctionName, MaxRetries,
                string.Join("\n", ex.Errors),
                code ?? "(brak kodu)");

            return UnprocessableEntity(new { errors = ex.Errors });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "<<< Nieoczekiwany błąd [{Name}]", request.FunctionName);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
