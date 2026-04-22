using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace cadll.Services;

public static class ZwcadApiService
{
    private static string? _cachedSummary;
    private static readonly object _lock = new();

    public static string GetApiSummary(string zwcadDllDir)
    {
        if (_cachedSummary is not null) return _cachedSummary;
        lock (_lock)
        {
            if (_cachedSummary is not null) return _cachedSummary;
            _cachedSummary = BuildSummary(zwcadDllDir);
            return _cachedSummary;
        }
    }

    private static string BuildSummary(string zwcadDllDir)
    {
        if (!Directory.Exists(zwcadDllDir))
            return "(katalog DLL ZWCAD nie istnieje)";

        var zwcadDlls = Directory.GetFiles(zwcadDllDir, "*.dll");
        if (zwcadDlls.Length == 0)
            return "(brak plików DLL w katalogu ZWCAD)";

        // Zbierz DLL-ki ze WSZYSTKICH shared frameworks (.NETCore + WindowsDesktop/WPF + AspNetCore)
        var allRuntimeDlls = CollectAllRuntimeDlls();

        var resolver = new GracefulResolver([.. allRuntimeDlls, .. zwcadDlls]);
        var sb = new StringBuilder();

        // Tylko DatabaseServices dostaje pełne składowe — reszta tylko nazwy typów
        var fullDetailNamespaces = new HashSet<string>
        {
            "ZwSoft.ZwCAD.DatabaseServices",
        };

        sb.AppendLine("Typy i składowe wyekstrahowane z DLL-ek ZWCAD.");
        sb.AppendLine("Używaj WYŁĄCZNIE typów i składowych z tej listy.");
        sb.AppendLine();

        using var mlc = new MetadataLoadContext(resolver);

        // Zbierz wszystkie typy ze wszystkich DLL-ek ZWCAD
        var allTypes = new List<Type>();
        foreach (var dllPath in zwcadDlls.OrderBy(p => p))
        {
            try
            {
                var asm = mlc.LoadFromAssemblyPath(dllPath);
                allTypes.AddRange(asm.GetExportedTypes()
                    .Where(t => t.Namespace?.StartsWith("ZwSoft.ZwCAD") == true && t.IsPublic));
            }
            catch { continue; }
        }

        var groups = allTypes
            .GroupBy(t => t.Namespace!)
            .OrderBy(g => g.Key);

        foreach (var nsGroup in groups)
        {
            sb.AppendLine($"### {nsGroup.Key}");

            var includeMembers = fullDetailNamespaces.Contains(nsGroup.Key);

            foreach (var type in nsGroup.OrderBy(t => t.Name))
            {
                try
                {
                    // Pełne składowe tylko dla konkretnych klas (nie abstrakcyjnych, nie interfejsów)
                    var fullDetail = includeMembers && !type.IsAbstract && !type.IsInterface;
                    if (fullDetail)
                        AppendType(sb, type);
                    else
                        sb.AppendLine($"  {type.Name}"); // tylko nazwa
                }
                catch
                {
                    sb.AppendLine($"  {type.Name}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendType(StringBuilder sb, Type type)
    {
        var kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : type.IsValueType ? "struct"
            : "class";

        var members = new List<string>();

        if (type.IsEnum)
        {
            members.AddRange(type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => f.Name));
        }
        else
        {
            // Properties — Name:ReturnType (rw) or Name:ReturnType (r)
            foreach (var prop in type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .OrderBy(p => p.Name))
            {
                try
                {
                    var rw = prop.CanWrite ? "rw" : "r";
                    members.Add($"{prop.Name}:{FriendlyName(prop.PropertyType)}({rw})");
                }
                catch { /* skip */ }
            }

            // Methods — Name(ParamTypes):ReturnType
            var skipMethods = new HashSet<string>
            {
                "ToString", "GetHashCode", "Equals", "GetType",
                "MemberwiseClone", "Finalize", "GetObjectData"
            };

            foreach (var method in type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => !m.IsSpecialName && !skipMethods.Contains(m.Name))
                .OrderBy(m => m.Name)
                .DistinctBy(m => m.Name))
            {
                try
                {
                    var ret = FriendlyName(method.ReturnType);
                    var parms = string.Join(",", method.GetParameters()
                        .Select(p => FriendlyName(p.ParameterType)));
                    members.Add($"{method.Name}({parms}):{ret}");
                }
                catch { /* skip */ }
            }
        }

        // Compact single-line format: TypeName (kind): member1, member2, ...
        // Cap at 8 members to keep prompt within token limits (~30k TPM)
        const int MaxMembers = 8;
        var capped = members.Count > MaxMembers ? members.Take(MaxMembers).Append("...") : members;
        var memberStr = members.Count > 0 ? ": " + string.Join(", ", capped) : string.Empty;
        sb.AppendLine($"  {type.Name} ({kind}){memberStr}");
    }

    private static string FriendlyName(Type t)
    {
        try
        {
            if (t.FullName == "System.Void") return "void";
            if (t.IsGenericType)
            {
                var name = t.Name[..t.Name.IndexOf('`')];
                var args = string.Join(",", t.GetGenericArguments().Select(FriendlyName));
                return $"{name}<{args}>";
            }
            return t.Name;
        }
        catch { return t.Name; }
    }

    /// <summary>
    /// Zbiera DLL-ki ze wszystkich shared frameworks zainstalowanych obok bieżącego runtime
    /// (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App, Microsoft.AspNetCore.App).
    /// </summary>
    private static List<string> CollectAllRuntimeDlls()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        // 1. Aktualny runtime (Microsoft.NETCore.App)
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var f in Directory.GetFiles(runtimeDir, "*.dll"))
            if (seen.Add(Path.GetFileName(f))) result.Add(f);

        // 2. Szukamy katalogu shared (np. C:\Program Files\dotnet\shared\)
        //    Struktura: dotnet/shared/<framework>/<version>/
        var sharedDir = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        if (Directory.Exists(sharedDir))
        {
            foreach (var frameworkDir in Directory.GetDirectories(sharedDir))
            {
                var latestVersion = Directory
                    .GetDirectories(frameworkDir)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                if (latestVersion is null) continue;

                foreach (var f in Directory.GetFiles(latestVersion, "*.dll"))
                    if (seen.Add(Path.GetFileName(f))) result.Add(f);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolver który zwraca null zamiast rzucać dla nieznanych assemblies —
    /// MetadataLoadContext po prostu pomija nierozwiązane zależności.
    /// </summary>
    private sealed class GracefulResolver(IReadOnlyList<string> dllPaths) : MetadataAssemblyResolver
    {
        private readonly Dictionary<string, string> _map = dllPaths
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
        {
            if (assemblyName.Name is null) return null;
            if (!_map.TryGetValue(assemblyName.Name, out var path)) return null;
            try { return context.LoadFromAssemblyPath(path); }
            catch { return null; }
        }
    }
}
