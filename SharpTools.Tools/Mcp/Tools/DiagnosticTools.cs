using System.ComponentModel;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Mcp.Models;
using SharpTools.Tools.Services;

namespace SharpTools.Tools.Mcp.Tools;

// Marker class for ILogger<T> category specific to DiagnosticTools
public class DiagnosticToolsLogCategory { }

[McpServerToolType]
public static class DiagnosticTools
{
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(GetCompilationDiagnostics), Idempotent = true, Destructive = false, OpenWorld = false, ReadOnly = true)]
    [Description("プロジェクトまたはファイルのコンパイル診断情報を取得")]
    public static async Task<object> GetCompilationDiagnostics(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeAnalysisService codeAnalysisService,
        ILogger<DiagnosticToolsLogCategory> logger,
        [Description("ソリューション(.sln)またはプロジェクト(.csproj)ファイルのパス")] string solutionOrProjectPath,
        [Description("診断対象のスコープ (solution/project/file)")] string scope = "project",
        [Description("ファイルパス (scope=fileの場合必須)")] string? filePath = null,
        [Description("取得する診断レベル (error/warning/all)")] string severity = "error",
        [Description("最大結果数")] int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            
            ErrorHandlingHelpers.ValidateStringParameter(solutionOrProjectPath, nameof(solutionOrProjectPath), logger);
            ErrorHandlingHelpers.ValidateStringParameter(scope, nameof(scope), logger);

            logger.LogInformation("Executing '{GetCompilationDiagnostics}' for scope: {Scope}, severity: {Severity}",
                nameof(GetCompilationDiagnostics), scope, severity);

            var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(solutionOrProjectPath);

            try
            {
                var result = new CompilationDiagnosticsResult
                {
                    Scope = scope
                };

                // Get diagnostics based on scope
                List<Diagnostic> allDiagnostics;
                
                switch (scope.ToLower())
                {
                    case "solution":
                        allDiagnostics = await GetSolutionDiagnosticsAsync(workspace, context, cancellationToken);
                        break;
                        
                    case "project":
                        allDiagnostics = await GetProjectDiagnosticsAsync(workspace, context, solutionOrProjectPath, cancellationToken);
                        break;
                        
                    case "file":
                        if (string.IsNullOrEmpty(filePath))
                        {
                            throw new ArgumentException("filePathパラメータはscope=fileの場合必須です");
                        }
                        allDiagnostics = await GetFileDiagnosticsAsync(workspace, context, filePath, cancellationToken);
                        break;
                        
                    default:
                        throw new ArgumentException($"無効なscope: {scope}。'solution', 'project', 'file'のいずれかを指定してください");
                }

                // Filter diagnostics by severity
                var filteredDiagnostics = allDiagnostics
                    .Where(d => severity.ToLower() switch
                    {
                        "error" => d.Severity == DiagnosticSeverity.Error,
                        "warning" => d.Severity >= DiagnosticSeverity.Warning,
                        "all" => true,
                        _ => d.Severity == DiagnosticSeverity.Error
                    })
                    .Take(maxResults)
                    .ToList();

                // Convert to DiagnosticDetail
                var affectedFiles = new HashSet<string>();
                
                foreach (var diag in filteredDiagnostics)
                {
                    var detail = new DiagnosticDetail
                    {
                        Id = diag.Id,
                        Severity = diag.Severity.ToString(),
                        Category = ContextInjectors.GetDiagnosticCategory(diag.Id),
                        Message = diag.GetMessage()
                    };

                    if (diag.Location.IsInSource)
                    {
                        var lineSpan = diag.Location.GetLineSpan();
                        detail.Location = new DiagnosticLocation
                        {
                            FilePath = lineSpan.Path,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1
                        };
                        
                        // Extract file name for summary
                        var fileName = Path.GetFileName(lineSpan.Path);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            affectedFiles.Add(fileName);
                        }
                    }

                    detail.CanAutoFix = CanAutoFix(diag.Id);
                    detail.SuggestedActions = GetSuggestedActions(diag);

                    result.Diagnostics.Add(detail);
                }

                // Build summary
                result.Summary = new DiagnosticsSummary
                {
                    TotalDiagnostics = result.Diagnostics.Count,
                    ErrorCount = result.Diagnostics.Count(d => d.Severity == "Error"),
                    WarningCount = result.Diagnostics.Count(d => d.Severity == "Warning"),
                    AffectedFileCount = affectedFiles.Count,
                    AffectedFiles = affectedFiles.ToList()
                };

                stopwatch.Stop();
                result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

                return result;
            }
            finally
            {
                workspace?.Dispose();
            }
        }, logger, nameof(GetCompilationDiagnostics), cancellationToken);
    }

    private static async Task<List<Diagnostic>> GetSolutionDiagnosticsAsync(
        MSBuildWorkspace workspace, object context, CancellationToken cancellationToken)
    {
        var diagnostics = new List<Diagnostic>();
        Solution solution;

        if (context is Solution sol)
        {
            solution = sol;
        }
        else if (context is Project proj)
        {
            solution = proj.Solution;
        }
        else
        {
            var dynamicContext = (dynamic)context;
            solution = ((Project)dynamicContext.Project).Solution;
        }

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation != null)
            {
                diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken));
            }
        }

        return diagnostics;
    }

    private static async Task<List<Diagnostic>> GetProjectDiagnosticsAsync(
        MSBuildWorkspace workspace, object context, string solutionOrProjectPath, CancellationToken cancellationToken)
    {
        Project? project;

        if (context is Project proj)
        {
            project = proj;
        }
        else if (context is Solution solution)
        {
            // Find project in solution
            var projectName = Path.GetFileNameWithoutExtension(solutionOrProjectPath);
            project = solution.Projects.FirstOrDefault(p => p.Name == projectName);
            if (project == null)
            {
                throw new ArgumentException($"プロジェクト '{projectName}' が見つかりません");
            }
        }
        else
        {
            var dynamicContext = (dynamic)context;
            project = (Project)dynamicContext.Project;
        }

        var compilation = await project.GetCompilationAsync(cancellationToken);
        return compilation?.GetDiagnostics(cancellationToken).ToList() ?? new List<Diagnostic>();
    }

    private static async Task<List<Diagnostic>> GetFileDiagnosticsAsync(
        MSBuildWorkspace workspace, object context, string filePath, CancellationToken cancellationToken)
    {
        Document? document = null;
        
        if (context is Solution solution)
        {
            foreach (var project in solution.Projects)
            {
                document = project.Documents.FirstOrDefault(d => d.FilePath == filePath);
                if (document != null) break;
            }
        }
        else if (context is Project proj)
        {
            document = proj.Documents.FirstOrDefault(d => d.FilePath == filePath);
        }
        else
        {
            var dynamicContext = (dynamic)context;
            var project = (Project)dynamicContext.Project;
            document = project.Documents.FirstOrDefault(d => d.FilePath == filePath);
        }

        if (document == null)
        {
            throw new ArgumentException($"ファイル '{filePath}' が見つかりません");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        return semanticModel?.GetDiagnostics(cancellationToken: cancellationToken).ToList() ?? new List<Diagnostic>();
    }

    private static bool CanAutoFix(string diagnosticId)
    {
        return diagnosticId switch
        {
            "CS1002" => true, // ; expected
            "CS1003" => true, // Simple syntax errors
            "CS8019" => true, // Unnecessary using directive
            _ => false
        };
    }

    private static List<string> GetSuggestedActions(Diagnostic diagnostic)
    {
        var actions = new List<string>();

        switch (diagnostic.Id)
        {
            case "CS0103": // Name not found
                actions.Add("using文を追加してください");
                actions.Add("名前空間を確認してください");
                actions.Add("タイプミスがないか確認してください");
                break;
            case "CS1061": // Member not found
                actions.Add("メンバー名を確認してください");
                actions.Add("対象の型が正しいか確認してください");
                actions.Add("必要な拡張メソッドのusing文を追加してください");
                break;
            case "CS0246": // Type not found
                actions.Add("using文を追加してください");
                actions.Add("参照アセンブリを確認してください");
                actions.Add("NuGetパッケージが必要な可能性があります");
                break;
            case "CS1002": // ; expected
            case "CS1003": // Syntax error
                actions.Add("構文を確認してください");
                actions.Add($"{ToolHelpers.SharpToolPrefix}FindAndReplace で修正してください");
                break;
            case "CS0111": // Already defined member
                actions.Add("重複するメンバーを削除してください");
                actions.Add("パラメータの型を変更してオーバーロードにしてください");
                break;
        }

        return actions;
    }
}