using cadll.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection.PortableExecutable;

namespace cadll.Services;

public class CompilerService(ILogger<CompilerService> logger)
{
    private static readonly string NetFx48Dir =
        Path.Combine(AppContext.BaseDirectory, "NetFx48");

    private static readonly string ExtraLibsDir =
        Path.Combine(AppContext.BaseDirectory, "Libraries", "Extra");

    private static readonly HashSet<string> ExcludedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.EnterpriseServices.Wrapper.dll",
        "System.EnterpriseServices.Thunk.dll",
    };

    public Task<byte[]> CompileAsync(string sourceCode, string assemblyName, string platform = "zwcad")
    {
        LogEnvironment(platform);

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        // Szybki pre-check składni — bez referencji, błyskawiczny
        var syntaxErrors = syntaxTree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        if (syntaxErrors.Count > 0)
        {
            logger.LogWarning("Błędy składni (pre-check, bez kompilacji):\n{Errors}",
                string.Join("\n", syntaxErrors));
            throw new CompilationException(syntaxErrors);
        }

        if (!Directory.Exists(NetFx48Dir))
            throw new InvalidOperationException(
                $"Brak katalogu NetFx48/ w: {NetFx48Dir}");

        var netFxFiles = Directory.GetFiles(NetFx48Dir, "*.dll")
            .Where(f => !ExcludedAssemblies.Contains(Path.GetFileName(f)))
            .Where(IsManagedAssembly)
            .ToList();

        logger.LogInformation("NetFx48: {Count} managed assemblies załadowanych z {Dir}",
            netFxFiles.Count, NetFx48Dir);

        var netFxRefs = netFxFiles
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f));

        var platformFolder = NormalizePlatformFolder(platform);
        var cadDir = Path.Combine(AppContext.BaseDirectory, "Libraries", platformFolder);
        var cadFiles = Directory.Exists(cadDir)
            ? Directory.GetFiles(cadDir, "*.dll").ToList()
            : [];

        logger.LogInformation("CAD [{Platform}] DLL-ki: {Count} plików z {Dir}",
            platform, cadFiles.Count, cadDir);

        foreach (var f in cadFiles)
            logger.LogInformation("  CAD ref: {File}", Path.GetFileName(f));

        var zwcadRefs = cadFiles
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f));

        var extraFiles = Directory.Exists(ExtraLibsDir)
            ? Directory.GetFiles(ExtraLibsDir, "*.dll")
                .Where(IsManagedAssembly)
                .ToList()
            : [];

        if (extraFiles.Count > 0)
            logger.LogInformation("Extra libs: {Count} plików z {Dir}",
                extraFiles.Count, ExtraLibsDir);

        var extraRefs = extraFiles
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f));

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [syntaxTree],
            references: [.. netFxRefs, .. zwcadRefs, .. extraRefs],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithPlatform(Platform.X64)
        );

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        var warnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .Select(d => d.GetMessage())
            .ToList();

        if (warnings.Count > 0)
            logger.LogInformation("Kompilacja — ostrzeżenia ({Count}):\n{Warnings}",
                warnings.Count, string.Join("\n", warnings));

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();

            logger.LogWarning("Kompilacja nieudana. Błędy ({Count}):\n{Errors}",
                errors.Count, string.Join("\n", errors));

            throw new CompilationException(errors);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var bytes = ms.ToArray();
        logger.LogInformation("Kompilacja OK — {Assembly}.dll, rozmiar: {Size} bajtów",
            assemblyName, bytes.Length);

        return Task.FromResult(bytes);
    }

    private static string NormalizePlatformFolder(string platform) =>
        platform.ToLowerInvariant() switch
        {
            "autocad"  => "Autocad",
            "bricscad" => "Bricscad",
            "gstarcad" => "Gstarcad",
            _          => "Zwcad",
        };

    private void LogEnvironment(string platform)
    {
        logger.LogInformation("=== ŚRODOWISKO KOMPILACJI [platform={Platform}] ===", platform);
        logger.LogInformation("AppContext.BaseDirectory: {Dir}", AppContext.BaseDirectory);
        logger.LogInformation("NetFx48Dir: {Dir} | istnieje: {Exists}",
            NetFx48Dir, Directory.Exists(NetFx48Dir));

        if (Directory.Exists(NetFx48Dir))
            logger.LogInformation("NetFx48Dir — pliki: {Count}",
                Directory.GetFiles(NetFx48Dir, "*.dll").Length);

        var cadDir = Path.Combine(AppContext.BaseDirectory, "Libraries", NormalizePlatformFolder(platform));
        logger.LogInformation("CadDir [{Platform}]: {Dir} | istnieje: {Exists}",
            platform, cadDir, Directory.Exists(cadDir));
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return peReader.HasMetadata;
        }
        catch { return false; }
    }
}
