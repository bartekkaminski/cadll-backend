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
        _chat = new OpenAIClient(key).GetChatClient("gpt-4.1");
        _zwcadDllDir = Path.Combine(AppContext.BaseDirectory, "Libraries", "Zwcad");
    }

    public async Task<string> GenerateFunctionCodeAsync(string functionName, string prompt)
    {
        var apiSummary = ZwcadApiService.GetApiSummary(_zwcadDllDir);

        var systemMsg = SystemPromptTemplate
            .Replace("{FUNCTION_NAME}", functionName)
            .Replace("{ZWCAD_API}", $"=== ZWCAD API ===\n{apiSummary}");

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemMsg),
            new UserChatMessage(
                $"Nazwa funkcji: {functionName}\n\nOpis wymaganej funkcji:\n{prompt}")
        };

        var response = await _chat.CompleteChatAsync(messages);
        var text = response.Value.Content[0].Text;
        return ExtractCodeBlock(text);
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
