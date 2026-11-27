namespace YScriptEngine.Lsp.Abstractions;

public interface ILspProvider
{
    Task<IEnumerable<LspCompletionItem>> GetCompletionsAsync(string code, int cursorPosition, Type globalsType,
        CancellationToken token);

    Task<IEnumerable<LspDiagnostic>> GetDiagnosticsAsync(string code, Type globalsType, CancellationToken token = default);
    Task<string> FormatCodeAsync(string code, Type globalsType, CancellationToken token = default);
    Task<string?> GetHoverInfoAsync(string code, int cursorPosition, Type globalsType, CancellationToken token = default);

    Task<IEnumerable<LspSemanticToken>> GetSemanticTokensAsync(string code, Type globalsType, CancellationToken token = default);
}

public record LspCompletionItem(string Label, string Type, string Detail = "");

public record LspDiagnostic(int From, int To, string Message, string Severity, string Code);

public record LspQuickAction(
    string Title,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string NewText);

public record LspSemanticToken(int Start, int End, string Kind);