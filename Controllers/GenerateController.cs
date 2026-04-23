using System.IO.Compression;
using cadll.Models;
using cadll.Services;
using Microsoft.AspNetCore.Mvc;

namespace cadll.Controllers;

[ApiController]
public class GenerateController(
    JobStore jobs,
    IServiceScopeFactory scopeFactory,
    ILogger<GenerateController> logger) : ControllerBase
{
    private const int MaxRetries = 2;

    [HttpPost("api/generate")]
    public IActionResult Generate([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FunctionName))
            return BadRequest(new { error = "FunctionName jest wymagana." });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt jest wymagany." });

        var jobId = jobs.Create();
        logger.LogInformation(">>> Job {JobId} utworzony: funkcja={Name}", jobId, request.FunctionName);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedAi       = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
            var scopedCompiler = scope.ServiceProvider.GetRequiredService<CompilerService>();

            string? code = null;
            try
            {
                jobs.SetPhase(jobId, "generating");
                code = await scopedAi.GenerateFunctionCodeAsync(request.FunctionName, request.Prompt);

                logger.LogInformation(
                    "=== WYGENEROWANY KOD [{Name}] ===\n{Code}\n=== KONIEC KODU ===",
                    request.FunctionName, code);

                jobs.SetPhase(jobId, "compiling");

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        var dll = await scopedCompiler.CompileAsync(PrepareForCompilation(code), request.FunctionName);
                        logger.LogInformation("<<< Job {JobId} sukces: {Name}.dll ({Size} B) — próba {Attempt}",
                            jobId, request.FunctionName, dll.Length, attempt);
                        jobs.SetDone(jobId, PackZip(dll, request.FunctionName, code));
                        return;
                    }
                    catch (CompilationException ex) when (attempt < MaxRetries)
                    {
                        logger.LogWarning(
                            "Job {JobId} próba {Attempt}/{Max} nieudana. Błędy:\n{Errors}\nPonawiam z AI...",
                            jobId, attempt, MaxRetries, string.Join("\n", ex.Errors));

                        jobs.SetPhase(jobId, "fixing");
                        code = await scopedAi.FixCodeAsync(code, ex.Errors);

                        logger.LogInformation(
                            "=== POPRAWIONY KOD [{Name}] (próba {Next}) ===\n{Code}\n=== KONIEC ===",
                            request.FunctionName, attempt + 1, code);

                        jobs.SetPhase(jobId, "compiling");
                    }
                }

                var finalDll = await scopedCompiler.CompileAsync(PrepareForCompilation(code), request.FunctionName);
                logger.LogInformation("<<< Job {JobId} sukces: {Name}.dll ({Size} B) — ostatnia próba",
                    jobId, request.FunctionName, finalDll.Length);
                jobs.SetDone(jobId, PackZip(finalDll, request.FunctionName, code));
            }
            catch (CompilationException ex)
            {
                logger.LogWarning(
                    "<<< Job {JobId} błąd kompilacji po {Max} próbach:\n{Errors}",
                    jobId, MaxRetries, string.Join("\n", ex.Errors));
                jobs.SetError(jobId, "Błąd kompilacji.", ex.Errors);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "<<< Job {JobId} nieoczekiwany błąd", jobId);
                jobs.SetError(jobId, ex.Message);
            }
        });

        return Accepted(new { jobId });
    }

    [HttpGet("api/status/{jobId}")]
    public IActionResult Status(string jobId)
    {
        var job = jobs.Get(jobId);
        if (job is null) return NotFound(new { error = "Job nie istnieje lub wygasł." });

        return job.Status switch
        {
            JobStatus.Done  => Ok(new { status = "done",    phase = job.Phase }),
            JobStatus.Error => Ok(new { status = "error",   phase = job.Phase,
                                        error = job.ErrorMessage, errors = job.Errors }),
            _               => Ok(new { status = "pending", phase = job.Phase })
        };
    }

    [HttpGet("api/download/{jobId}")]
    public IActionResult Download(string jobId)
    {
        var job = jobs.Get(jobId);
        if (job is null)           return NotFound(new { error = "Job nie istnieje lub wygasł." });
        if (job.Status != JobStatus.Done) return BadRequest(new { error = "Plik nie jest jeszcze gotowy." });

        return File(job.ZipBytes!, "application/zip", $"{jobId}.zip");
    }

    private static readonly string ExtraLibsDir =
        Path.Combine(AppContext.BaseDirectory, "Libraries", "Extra");

    private const string AssemblyResolverSnippet = """

            AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
            {
                string dir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(Main).Assembly.Location), "Biblioteki");
                string f = System.IO.Path.Combine(
                    dir, new System.Reflection.AssemblyName(a.Name).Name + ".dll");
                return System.IO.File.Exists(f) ? System.Reflection.Assembly.LoadFrom(f) : null;
            };
    """;

    private static string PrepareForCompilation(string code)
    {
        if (!code.Contains("DocumentFormat.OpenXml", StringComparison.OrdinalIgnoreCase))
            return code;

        const string marker = "public void Initialize()";
        int methodPos = code.IndexOf(marker, StringComparison.Ordinal);
        if (methodPos < 0) return code;

        int bracePos = code.IndexOf('{', methodPos + marker.Length);
        if (bracePos < 0) return code;

        return code.Insert(bracePos + 1, AssemblyResolverSnippet);
    }

    private static byte[] PackZip(byte[] dllBytes, string functionName, string sourceCode)
    {
        var includeOpenXml = sourceCode.Contains("DocumentFormat.OpenXml",
            StringComparison.OrdinalIgnoreCase);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"{functionName}.dll", CompressionLevel.Fastest);
            using (var entryStream = entry.Open())
                entryStream.Write(dllBytes, 0, dllBytes.Length);

            if (includeOpenXml && Directory.Exists(ExtraLibsDir))
            {
                var openXmlLibs = new[]
                {
                    "DocumentFormat.OpenXml.dll",
                    "DocumentFormat.OpenXml.Framework.dll",
                };
                foreach (var name in openXmlLibs)
                {
                    var path = Path.Combine(ExtraLibsDir, name);
                    if (!System.IO.File.Exists(path)) continue;
                    var libEntry = zip.CreateEntry($"Biblioteki/{name}", CompressionLevel.Fastest);
                    using (var libStream = libEntry.Open())
                    using (var libFile = System.IO.File.OpenRead(path))
                        libFile.CopyTo(libStream);
                }
            }
        }
        return ms.ToArray();
    }
}
