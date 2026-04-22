using cadll.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection.PortableExecutable;

namespace cadll.Services;

public class CompilerService
{
    private static readonly string NetFx48Dir =
        Path.Combine(AppContext.BaseDirectory, "NetFx48");

    // Mixed-mode lub natywne assemblies — Roslyn nie może ich użyć jako referencji
    private static readonly HashSet<string> ExcludedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.EnterpriseServices.Wrapper.dll",
        "System.EnterpriseServices.Thunk.dll",
    };

    public Task<byte[]> CompileAsync(string sourceCode, string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        // Assemblies .NET Framework 4.8 skopiowane z NuGet do bin/NetFx48/
        if (!Directory.Exists(NetFx48Dir))
            throw new InvalidOperationException(
                $"Brak katalogu NetFx48/ w {NetFx48Dir}. " +
                "Przebuduj projekt (dotnet build) żeby MSBuild skopiował assemblies .NET Framework 4.8.");

        var netFxRefs = Directory.GetFiles(NetFx48Dir, "*.dll")
            .Where(f => !ExcludedAssemblies.Contains(Path.GetFileName(f)))
            .Where(IsManagedAssembly)
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f));

        // DLL-ki ZWCAD skopiowane do bin/Libraries/Zwcad/
        var zwcadDir = Path.Combine(AppContext.BaseDirectory, "Libraries", "Zwcad");
        var zwcadRefs = Directory.Exists(zwcadDir)
            ? Directory.GetFiles(zwcadDir, "*.dll")
                .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))
            : [];

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

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();
            throw new CompilationException(errors);
        }

        ms.Seek(0, SeekOrigin.Begin);
        return Task.FromResult(ms.ToArray());
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
