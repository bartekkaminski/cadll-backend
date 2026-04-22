using cadll.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection.PortableExecutable;

namespace cadll.Services;

public class CompilerService(ILogger<CompilerService> logger)
{
    private static readonly string NetFx48Dir =
        Path.Combine(AppContext.BaseDirectory, "NetFx48");

    private static readonly HashSet<string> ExcludedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.EnterpriseServices.Wrapper.dll",
        "System.EnterpriseServices.Thunk.dll",
    };

    public Task<byte[]> CompileAsync(string sourceCode, string assemblyName)
    {
        LogEnvironment();

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

        var zwcadDir = Path.Combine(AppContext.BaseDirectory, "Libraries", "Zwcad");
        var zwcadFiles = Directory.Exists(zwcadDir)
            ? Directory.GetFiles(zwcadDir, "*.dll").ToList()
            : [];

        logger.LogInformation("ZWCAD DLL-ki: {Count} plików z {Dir}",
            zwcadFiles.Count, zwcadDir);

        foreach (var f in zwcadFiles)
            logger.LogInformation("  ZWCAD ref: {File}", Path.GetFileName(f));

        var zwcadRefs = zwcadFiles
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f));

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [syntaxTree],
            references: [.. netFxRefs, .. zwcadRefs],
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

    private void LogEnvironment()
    {
        logger.LogInformation("=== ŚRODOWISKO KOMPILACJI ===");
        logger.LogInformation("AppContext.BaseDirectory: {Dir}", AppContext.BaseDirectory);
        logger.LogInformation("NetFx48Dir: {Dir} | istnieje: {Exists}",
            NetFx48Dir, Directory.Exists(NetFx48Dir));

        if (Directory.Exists(NetFx48Dir))
            logger.LogInformation("NetFx48Dir — pliki: {Count}",
                Directory.GetFiles(NetFx48Dir, "*.dll").Length);

        var zwcadDir = Path.Combine(AppContext.BaseDirectory, "Libraries", "Zwcad");
        logger.LogInformation("ZwcadDir: {Dir} | istnieje: {Exists}",
            zwcadDir, Directory.Exists(zwcadDir));
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
