using System.IO.Compression;
using cadll.Data;
using cadll.Data.Entities;
using cadll.Models;
using cadll.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace cadll.Controllers;

[ApiController]
public class GenerateController(
    JobStore jobs,
    IServiceScopeFactory scopeFactory,
    ILogger<GenerateController> logger) : ControllerBase
{
    private const int MaxRetries = 2;

    [HttpPost("api/generate")]
    [EnableRateLimiting("per-ip-daily")]
    public IActionResult Generate([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FunctionName))
            return BadRequest(new { error = "FunctionName jest wymagana." });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt jest wymagany." });

        var platform = string.IsNullOrWhiteSpace(request.Platform) ? "zwcad" : request.Platform.ToLowerInvariant();
        var userIp = (HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                      ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                      ?? "unknown").Split(',')[0].Trim();

        var jobId = jobs.Create();
        logger.LogInformation(">>> Job {JobId} utworzony: funkcja={Name} platform={Platform}", jobId, request.FunctionName, platform);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedAi       = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
            var scopedCompiler = scope.ServiceProvider.GetRequiredService<CompilerService>();
            var db             = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            string? code = null;
            int totalIn = 0, totalOut = 0, aiCalls = 0;
            var apiCallLog = new List<AiApiCall>();

            void Accumulate(CodeResult r, string operation)
            {
                totalIn += r.InputTokens; totalOut += r.OutputTokens; aiCalls++;
                apiCallLog.Add(new AiApiCall
                {
                    JobId        = Guid.Parse(jobId),
                    Operation    = operation,
                    AiModel      = r.AiModel,
                    InputTokens  = r.InputTokens,
                    OutputTokens = r.OutputTokens,
                    ResponseCode = r.Code,
                    CalledAt     = r.CalledAt,
                });
            }

            async Task SaveToDb(string outcome, string? finalCode)
            {
                logger.LogInformation(
                    "=== TOKENY SUMARYCZNIE [{Name}|{Outcome}] wywołań={Calls} in={In} out={Out} | łącznie={Total} ===",
                    request.FunctionName, outcome, aiCalls, totalIn, totalOut, totalIn + totalOut);
                try
                {
                    var job = new GenerationJob
                    {
                        Id                 = Guid.Parse(jobId),
                        UserIp             = userIp,
                        FunctionName       = request.FunctionName,
                        Prompt             = request.Prompt,
                        Platform           = platform,
                        Outcome            = outcome,
                        FinalCode          = finalCode,
                        TotalAiCalls       = aiCalls,
                        TotalInputTokens   = totalIn,
                        TotalOutputTokens  = totalOut,
                        AiApiCalls         = apiCallLog,
                    };
                    db.GenerationJobs.Add(job);
                    await db.SaveChangesAsync();
                }
                catch (Exception dbEx)
                {
                    logger.LogError(dbEx, "Błąd zapisu do bazy dla Job {JobId}", jobId);
                }
            }

            try
            {
                jobs.SetPhase(jobId, "generating");
                var genResult = await scopedAi.GenerateFunctionCodeAsync(request.FunctionName, request.Prompt, platform);
                Accumulate(genResult, "GenerateCode");
                code = genResult.Code;

                logger.LogInformation(
                    "=== WYGENEROWANY KOD [{Name}] ===\n{Code}\n=== KONIEC KODU ===",
                    request.FunctionName, code);

                jobs.SetPhase(jobId, "compiling");

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        var dll = await scopedCompiler.CompileAsync(PrepareForCompilation(code), request.FunctionName, platform);
                        logger.LogInformation("<<< Job {JobId} sukces: {Name}.dll ({Size} B) — próba {Attempt}",
                            jobId, request.FunctionName, dll.Length, attempt);
                        await SaveToDb("sukces", code);
                        jobs.SetDone(jobId, PackZip(dll, request.FunctionName, code, platform));
                        return;
                    }
                    catch (CompilationException ex) when (attempt < MaxRetries)
                    {
                        logger.LogWarning(
                            "Job {JobId} próba {Attempt}/{Max} nieudana. Błędy:\n{Errors}\nPonawiam z AI...",
                            jobId, attempt, MaxRetries, string.Join("\n", ex.Errors));

                        jobs.SetPhase(jobId, "fixing");
                        var fixResult = await scopedAi.FixCodeAsync(code, ex.Errors, platform);
                        Accumulate(fixResult, "FixCode");
                        code = fixResult.Code;

                        logger.LogInformation(
                            "=== POPRAWIONY KOD [{Name}] (próba {Next}) ===\n{Code}\n=== KONIEC ===",
                            request.FunctionName, attempt + 1, code);

                        jobs.SetPhase(jobId, "compiling");
                    }
                }

                var finalDll = await scopedCompiler.CompileAsync(PrepareForCompilation(code), request.FunctionName, platform);
                logger.LogInformation("<<< Job {JobId} sukces: {Name}.dll ({Size} B) — ostatnia próba",
                    jobId, request.FunctionName, finalDll.Length);
                await SaveToDb("sukces", code);
                jobs.SetDone(jobId, PackZip(finalDll, request.FunctionName, code, platform));
            }
            catch (CompilationException ex)
            {
                logger.LogWarning(
                    "<<< Job {JobId} błąd kompilacji po {Max} próbach:\n{Errors}",
                    jobId, MaxRetries, string.Join("\n", ex.Errors));
                await SaveToDb("błąd-kompilacji", null);
                jobs.SetError(jobId, "Błąd kompilacji.", ex.Errors);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "<<< Job {JobId} nieoczekiwany błąd", jobId);
                await SaveToDb("błąd", null);
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

    private static readonly string ExtraLibsNetCoreDir =
        Path.Combine(AppContext.BaseDirectory, "Libraries", "Extra.NetCore");

    private static readonly HashSet<string> NetCorePlatforms =
        new(StringComparer.OrdinalIgnoreCase) { "gstarcad" };

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

    private static byte[] PackZip(byte[] dllBytes, string functionName, string sourceCode, string platform)
    {
        var includeOpenXml = sourceCode.Contains("DocumentFormat.OpenXml",
            StringComparison.OrdinalIgnoreCase);

        var extraDir = NetCorePlatforms.Contains(platform) ? ExtraLibsNetCoreDir : ExtraLibsDir;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"{functionName}.dll", CompressionLevel.Fastest);
            using (var entryStream = entry.Open())
                entryStream.Write(dllBytes, 0, dllBytes.Length);

            if (includeOpenXml && Directory.Exists(extraDir))
            {
                var openXmlLibs = new[]
                {
                    "DocumentFormat.OpenXml.dll",
                    "DocumentFormat.OpenXml.Framework.dll",
                };
                foreach (var name in openXmlLibs)
                {
                    var path = Path.Combine(extraDir, name);
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
