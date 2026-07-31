using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using YScriptEngine.Lsp.Abstractions;

namespace YScriptEngine.Lsp.Roslyn;

public class RoslynLspProvider(ScriptOptions sharedScriptOptions, ILogger<RoslynLspProvider> logger) : ILspProvider
{
    private readonly ConcurrentDictionary<Type, (Solution Solution, ProjectId ProjectId)> _cache = new();

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
            .WithUsings(sharedScriptOptions.Imports);
            
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
            metadataReferences: sharedScriptOptions.MetadataReferences, 
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
            Detail: item.InlineDescription ?? ""
        ));
    }

    public async Task<IEnumerable<LspDiagnostic>> GetDiagnosticsAsync(string code, Type payloadType, CancellationToken token)
    {
        var document = CreateTransientDocument(code, payloadType);
        
        var compilation = await document.Project.GetCompilationAsync(token);
        if (compilation == null) return [];

        var allDiagnostics = compilation.GetDiagnostics(token);

        return allDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning)
            .Select(d => new LspDiagnostic(
                From: d.Location.SourceSpan.Start,
                To: d.Location.SourceSpan.End,
                Message: d.GetMessage(),
                Severity: d.Severity.ToString().ToLower()
            ));
    }
}