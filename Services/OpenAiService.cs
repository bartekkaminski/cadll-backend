using OpenAI;
using OpenAI.Chat;

namespace cadll.Services;

public class OpenAiService
{
    private readonly ChatClient _chat;
    private readonly string _zwcadDllDir;

    private const string SystemPromptTemplate = """
        Jesteś ekspertem od tworzenia wtyczek ZWCAD w C#.
        Na podstawie opisu użytkownika wygeneruj kompletną, gotową do kompilacji klasę C#.

        PRZED WYSŁANIEM ODPOWIEDZI wykonaj wewnętrzną kontrolę jakości:
        - Przeczytaj wygenerowany kod od początku do końca
        - Upewnij się że każda metoda, pętla i blok logiczny jest poprawnie zamknięty
        - Upewnij się że każdy wzorzec regex jest przetestowany mentalnie na przykładowych danych
        - Upewnij się że typy i metody istnieją w ZWCAD API (lista poniżej)
        - Upewnij się że nie ma żadnych TODO, placeholderów ani niekompletnych sekcji
        - Kod musi skompilować się i działać poprawnie za pierwszym razem — nie ma możliwości poprawek

        Zasady (OBOWIĄZKOWE):
        1. Przestrzeń nazw: cadll.Generated
        2. Nazwa klasy = Main, MUSI implementować IExtensionApplication
           (public class Main : IExtensionApplication)
           z metodami Initialize() i Terminate()
        3. Jedna publiczna statyczna metoda z atrybutem [CommandMethod("{FUNCTION_NAME}")]
           i nazwą dokładnie {FUNCTION_NAME}
           (klasa "Main" i metoda "{FUNCTION_NAME}" to różne nazwy — brak błędu CS0542)
        4. OBOWIĄZKOWY zestaw using — ZAWSZE dołącz wszystkie poniższe:
             using System;
             using System.Collections.Generic;
             using System.Linq;
             using ZwSoft.ZwCAD.ApplicationServices;
             using ZwSoft.ZwCAD.Colors;
             using ZwSoft.ZwCAD.DatabaseServices;
             using ZwSoft.ZwCAD.EditorInput;
             using ZwSoft.ZwCAD.Geometry;
             using ZwSoft.ZwCAD.GraphicsInterface;
             using ZwSoft.ZwCAD.Runtime;
           Jeśli używasz typów z innych przestrzeni ZwSoft.ZwCAD.*, dołącz je też.
        5. Kod musi być kompletny i kompilować się bez błędów — bez TODO, bez placeholderów
        6. Odpowiedz WYŁĄCZNIE blokiem kodu ```csharp ... ``` — żadnego tekstu poza nim
        7. KRYTYCZNE: używaj WYŁĄCZNIE typów i składowych z sekcji "ZWCAD API" poniżej.
           Ta lista pochodzi z rzeczywistych DLL-ek ZWCAD. Jeśli typ lub metoda nie ma jej
           na liście — NIE ISTNIEJE w tej wersji ZWCAD i spowoduje błąd kompilacji.
           Znane RÓŻNICE między AutoCAD i ZWCAD (częste błędy GPT):
             - Dimension.TextOverride  → NIE ISTNIEJE w ZWCAD, użyj Dimension.DimensionText
             - MText.Text              → w ZWCAD zwraca MText, użyj MText.Contents (zwraca string)
             - MultiLeader             → NIE ISTNIEJE, poprawna nazwa to MLeader
             - Table.Rows              → zwraca RowsCollection (NIE int), liczba wierszy: table.Rows.Count
             - Table.Columns           → zwraca ColumnsCollection (NIE int), liczba kolumn: table.Columns.Count
           Znane BŁĘDY przy przetwarzaniu tekstu ZWCAD:
             - NIGDY nie dziel tekstu po znaku backslash '\' — MText używa \P jako separator linii
               ZŁE:   txt.Split(new[] { '\n', '\\', ';' })   ← niszczy wzorce jak (20)#8\P8sztuk
               DOBRE: najpierw zamień "\\P" i "\\p" na "\n", potem dziel tylko po '\n'
               Przykład:
                 string normalized = txt.Replace("\\P", "\n").Replace("\\p", "\n");
                 string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
           Znane BŁĘDY C# (częste pomyłki GPT) — StringComparer vs StringComparison:
             StringComparer to KLASA używana WYŁĄCZNIE w konstruktorach kolekcji.
             StringComparison to ENUM używany w metodach string.
             NIGDY nie używaj StringComparer w wywołaniach metod string — zawsze StringComparison!
             BŁĘDNE (nie kompiluje się):
               str.IndexOf("x", StringComparer.OrdinalIgnoreCase)     ← StringComparer zamiast int
               string.Compare(a, b, StringComparer.OrdinalIgnoreCase) ← brak takiego przeciążenia
               string.Equals(a, b, StringComparer.OrdinalIgnoreCase)  ← brak takiego przeciążenia
               str.Contains("x", StringComparer.OrdinalIgnoreCase)    ← brak takiego przeciążenia
             POPRAWNE:
               str.IndexOf("x", StringComparison.OrdinalIgnoreCase)
               string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
               string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
               str.Contains("x", StringComparison.OrdinalIgnoreCase)
             StringComparer TYLKO gdy klucz to dokładnie 'string':
               new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)   ← OK
               new HashSet<string>(StringComparer.OrdinalIgnoreCase)           ← OK
             NIGDY z kluczem tuple — tuple NIE jest string:
               new Dictionary<(string, string), int>(StringComparer.OrdinalIgnoreCase) ← BŁĄD kompilacji!
               new Dictionary<(string, string), int>()                                 ← POPRAWNE

        Wzorzec struktury (OBOWIĄZKOWY — klasa MUSI implementować IExtensionApplication):
        ```csharp
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using ZwSoft.ZwCAD.ApplicationServices;
        using ZwSoft.ZwCAD.Colors;
        using ZwSoft.ZwCAD.DatabaseServices;
        using ZwSoft.ZwCAD.EditorInput;
        using ZwSoft.ZwCAD.Geometry;
        using ZwSoft.ZwCAD.GraphicsInterface;
        using ZwSoft.ZwCAD.Runtime;

        namespace cadll.Generated
        {
            public class Main : IExtensionApplication
            {
                public void Initialize()
                {
                    // wywoływane przez ZWCAD przy ładowaniu DLL przez NETLOAD
                    Application.DocumentManager.MdiActiveDocument?
                        .Editor.WriteMessage("\ncadll: wtyczka załadowana pomyślnie.");
                }

                public void Terminate() { }

                [CommandMethod("PrzykladowaFunkcja")]
                public static void PrzykladowaFunkcja()
                {
                    Document doc = Application.DocumentManager.MdiActiveDocument;
                    Editor ed = doc.Editor;
                    Database db = doc.Database;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // ... pełna implementacja ...
                        tr.Commit();
                    }
                }
            }
        }
        ```

        {ZWCAD_API}
        """;

    public OpenAiService()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "Brak zmiennej środowiskowej OPENAI_API_KEY. " +
                "Ustaw ją przed uruchomieniem aplikacji.");
        _chat = new OpenAIClient(key).GetChatClient("gpt-5.4");
        _zwcadDllDir = Path.Combine(AppContext.BaseDirectory, "Libraries", "Zwcad");
    }

    public async Task<string> GenerateFunctionCodeAsync(string functionName, string prompt)
    {
        // var apiSummary = ZwcadApiService.GetApiSummary(_zwcadDllDir);

        var systemMsg = SystemPromptTemplate
            .Replace("{FUNCTION_NAME}", functionName)
            // .Replace("{ZWCAD_API}", $"=== ZWCAD API ===\n{apiSummary}");
            .Replace("{ZWCAD_API}", "");

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemMsg),
            new UserChatMessage(
                $"Nazwa funkcji: {functionName}\n\nOpis wymaganej funkcji:\n{prompt}")
        };

        var response = await _chat.CompleteChatAsync(messages);
        var text = response.Value.Content[0].Text;
        var code = ExtractCodeBlock(text);
        return FixCommonMistakes(code);
    }

    // Automatyczna korekta typowych pomyłek GPT żeby nie blokować kompilacji
    private static string FixCommonMistakes(string code)
    {
        // StringComparer w metodach string → StringComparison
        var methodsWithStringComparison = new[]
        {
            "IndexOf", "LastIndexOf", "StartsWith", "EndsWith",
            "Contains", "Replace", "Compare", "Equals"
        };

        foreach (var method in methodsWithStringComparison)
        {
            // np. .IndexOf("x", StringComparer.X) → .IndexOf("x", StringComparison.X)
            code = System.Text.RegularExpressions.Regex.Replace(
                code,
                $@"(\.{method}\s*\([^)]*),\s*StringComparer\.(\w+)\)",
                $"$1, StringComparison.$2)");
        }

        // string.Compare(a, b, StringComparer.X) → string.Compare(a, b, StringComparison.X)
        code = System.Text.RegularExpressions.Regex.Replace(
            code,
            @"(string\s*\.\s*(?:Compare|Equals)\s*\([^)]*),\s*StringComparer\.(\w+)\)",
            "$1, StringComparison.$2)");

        // Dictionary/SortedDictionary z kluczem tuple i StringComparer → usuń StringComparer
        // new Dictionary<(string, string), int>(StringComparer.X) → new Dictionary<(string, string), int>()
        code = System.Text.RegularExpressions.Regex.Replace(
            code,
            @"(new\s+(?:Sorted)?Dictionary\s*<\s*\([^)]+\)[^>]*>\s*\()\s*StringComparer\.\w+\s*\)",
            "$1)");

        return code;
    }

    private static string ExtractCodeBlock(string response)
    {
        var fence = "```csharp";
        var start = response.IndexOf(fence, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start = response.IndexOf('\n', start) + 1;
            var end = response.IndexOf("```", start);
            if (end >= 0)
                return response[start..end].Trim();
        }

        start = response.IndexOf("```");
        if (start >= 0)
        {
            start = response.IndexOf('\n', start) + 1;
            var end = response.IndexOf("```", start);
            if (end >= 0)
                return response[start..end].Trim();
        }

        return response.Trim();
    }
}
