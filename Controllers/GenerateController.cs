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
    [HttpPost("api/generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FunctionName))
            return BadRequest(new { error = "FunctionName jest wymagana." });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt jest wymagany." });

        string? code = null;
        try
        {
            code = await openAi.GenerateFunctionCodeAsync(request.FunctionName, request.Prompt);

            logger.LogDebug("=== WYGENEROWANY KOD [{Name}] ===\n{Code}\n=== KONIEC KODU ===",
                request.FunctionName, code);

            var dll = await compiler.CompileAsync(code, request.FunctionName);
            return File(dll, "application/octet-stream", $"{request.FunctionName}.dll");
        }
        catch (CompilationException ex)
        {
            if (code is not null)
                logger.LogWarning("Kompilacja nieudana [{Name}]. Błędy:\n{Errors}\n\nKod:\n{Code}",
                    request.FunctionName,
                    string.Join("\n", ex.Errors),
                    code);
            return UnprocessableEntity(new { errors = ex.Errors });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
