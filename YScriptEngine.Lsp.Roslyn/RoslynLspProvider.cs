using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YScriptEngine.Lsp.Abstractions;

namespace YScriptEngine.Lsp.Roslyn;

public class RoslynLspProvider(IServiceProvider serviceProvider, ILogger<RoslynLspProvider> logger) : ILspProvider
{
    private readonly ConcurrentDictionary<Type, (Solution Solution, ProjectId ProjectId)> _cache = new();
    private ScriptOptions SharedScriptOptions => serviceProvider.GetRequiredService<ScriptOptions>();
    public void InvalidateCache() => _cache.Clear();

    public void PreCacheBaseSolution(Type globalsType)
    {
        using var workspace = new AdhocWorkspace();
        var typeName = globalsType.Name;
        if (globalsType.IsGenericType)
        {
            typeName = globalsType.GetGenericArguments()[0].Name;
        }
        
        logger.LogDebug("Registering Type: {TypeName}", typeName);
        
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, scriptClassName: "Submission#0", concurrentBuild: false, metadataImportOptions: MetadataImportOptions.Public)
            .WithUsings(SharedScriptOptions.Imports);
            
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest, 
            kind: SourceCodeKind.Script,
            documentationMode: DocumentationMode.None );
        var projectId = ProjectId.CreateNewId();

        var projectInfo = ProjectInfo.Create(
            id: projectId, 
            version: VersionStamp.Create(), 
            name: $"Project_{typeName}", 
            assemblyName: $"Assembly_{typeName}",
            language: LanguageNames.CSharp, 
            compilationOptions: compilationOptions, 
            parseOptions: parseOptions,
            metadataReferences: SharedScriptOptions.MetadataReferences, 
            isSubmission: true, 
            hostObjectType: globalsType
        );

        _cache[globalsType] = (workspace.CurrentSolution.AddProject(projectInfo), projectId);
    }

    private Document CreateTransientDocument(string code, Type payloadType)
    {
        if (!_cache.TryGetValue(payloadType, out var template))
        {
            // If you're using this "correctly" you shouldn't have to worry too much
            // I should have made it throw, but was too much effort. :)
            PreCacheBaseSolution(payloadType);
            return CreateTransientDocument(code, payloadType);
        }
        
        var docId = DocumentId.CreateNewId(template.ProjectId);
        var ephemeralSolution = template.Solution.AddDocument(docId, "script.csx", SourceText.From(code));
    
        return ephemeralSolution.GetDocument(docId)!;
    }

    public async Task<IEnumerable<LspCompletionItem>> GetCompletionsAsync(string code, int cursorPosition, Type payloadType, CancellationToken token)
    {
        var document = CreateTransientDocument(code, payloadType);
        var completionService = CompletionService.GetService(document);
        if (completionService == null) return [];

        var completions = await completionService.GetCompletionsAsync(document, cursorPosition, cancellationToken: token);
        if (completions.ItemsList.Count == 0) return [];

        return completions.ItemsList.Select(item => new LspCompletionItem(
            Label: item.DisplayText,
            Type: item.Tags.FirstOrDefault()?.ToLower() ?? "variable",
            Detail: item.InlineDescription
        ));
    }

    public async Task<IEnumerable<LspDiagnostic>> GetDiagnosticsAsync(string code, Type payloadType, CancellationToken token)
    {
        var document = CreateTransientDocument(code, payloadType);
        
        var compilation = await document.Project.GetCompilationAsync(token);
        if (compilation == null) return [];

        var allDiagnostics = compilation.GetDiagnostics(token);

        return allDiagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => new LspDiagnostic(
                From: d.Location.SourceSpan.Start,
                To: d.Location.SourceSpan.End,
                Message: d.GetMessage(),
                Severity: d.Severity.ToString().ToLower(),
                Code: d.Id
            ));
    }
    
    public async Task<string> FormatCodeAsync(string code, Type payloadType, CancellationToken token)
    {
        var document = CreateTransientDocument(code, payloadType);
        var formattedDocument = await Formatter.FormatAsync(document, cancellationToken: token);
        var formattedText = await formattedDocument.GetTextAsync(token);
        return formattedText.ToString();
    }
    
    public async Task<string?> GetHoverInfoAsync(string code, int cursorPosition, Type payloadType, CancellationToken token)
    {
        var document = CreateTransientDocument(code, payloadType);
        var quickInfoService = QuickInfoService.GetService(document);
        if (quickInfoService == null) return null;

        var quickInfo = await quickInfoService.GetQuickInfoAsync(document, cursorPosition, token);
        if (quickInfo == null) return null;
        
        var sectionsText = quickInfo.Sections.Select(s => s.Text);
        return string.Join("\n\n", sectionsText);
    }
    
    public async Task<IEnumerable<LspQuickAction>> GetQuickActionsAsync(
        string code,
        int startOffset,
        int endOffset,
        Type payloadType,
        CancellationToken token)
    {
        var document = CreateTransientDocument(code, payloadType);
        var completionService = CompletionService.GetService(document);
        if (completionService is null) return [];

        var text = await document.GetTextAsync(token);
        var length = Math.Max(0, endOffset - startOffset);
        var targetWord = text.GetSubText(new TextSpan(startOffset, length)).ToString();
        if (string.IsNullOrWhiteSpace(targetWord)) return [];

        var completions = await completionService.GetCompletionsAsync(
            document,
            endOffset,
            cancellationToken: token);

        var actions = new List<LspQuickAction>();

        foreach (var item in completions.ItemsList)
        {
            if (item.DisplayText != targetWord) continue;

            var change = await completionService.GetChangeAsync(document, item, cancellationToken: token);

            foreach (var textChange in change.TextChanges)
            {
                if (textChange.Span.Start >= startOffset ||
                    textChange.NewText?.Contains("using ") != true) continue;
                
                var lineSpan = text.Lines.GetLinePositionSpan(textChange.Span);
                
                actions.Add(new LspQuickAction(
                    Title: textChange.NewText.Trim(),
                    StartLine: lineSpan.Start.Line + 1,
                    StartColumn: lineSpan.Start.Character + 1,
                    EndLine: lineSpan.End.Line + 1,
                    EndColumn: lineSpan.End.Character + 1,
                    NewText: textChange.NewText));
            }
        }

        return actions.DistinctBy(a => a.Title);
    }

    
}