namespace YScriptEngine.Lsp.Abstractions;

public interface ILspProvider
{
    Task<IEnumerable<LspCompletionItem>> GetCompletionsAsync(string code, int cursorPosition, Type globalsType, CancellationToken token);
    Task<IEnumerable<LspDiagnostic>> GetDiagnosticsAsync(string code, Type globalsType, CancellationToken token);
}

public record LspCompletionItem(string Label, string Type, string Detail = "");
public record LspDiagnostic(int From, int To, string Message, string Severity);