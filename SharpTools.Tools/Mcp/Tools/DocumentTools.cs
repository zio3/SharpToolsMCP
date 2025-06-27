using Microsoft.CodeAnalysis;
using ModelContextProtocol;
using SharpTools.Tools.Services;
using SharpTools.Tools.Mcp;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Mcp.Helpers;

namespace SharpTools.Tools.Mcp.Tools;

// Marker class for ILogger<T> category specific to DocumentTools
public class DocumentToolsLogCategory { }

[McpServerToolType]
public static class DocumentTools {

// Helper method to determine if a file is a supported code file that should be checked for compilation errors
private static bool IsSupportedCodeFile(string filePath) {
if (string.IsNullOrEmpty(filePath)) {
return false;
}

var extension = Path.GetExtension(filePath).ToLowerInvariant();
return extension switch {
".cs" => true,    // C# files
".csx" => true,   // C# script files
".vb" => true,    // Visual Basic files
".fs" => true,    // F# files
".fsx" => true,   // F# script files
".fsi" => true,   // F# signature files
_ => false
};
}

[McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ReadTypesFromRoslynDocument), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
[Description("🔍 .NET専用 - .cs/.sln/.csprojファイルのみ対応。Roslynを使用したC#タイプ解析")]
public static async Task<object> ReadTypesFromRoslynDocument(
StatelessWorkspaceFactory workspaceFactory,
ICodeAnalysisService codeAnalysisService,
ILogger<DocumentToolsLogCategory> logger,
[Description("C#ファイル(.cs)の絶対パス")] string filePath,
CancellationToken cancellationToken) {
return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
// 🔍 .NET関連ファイル検証（最優先実行）
CSharpFileValidationHelper.ValidateDotNetFileForRoslyn(filePath, nameof(ReadTypesFromRoslynDocument), logger);

ErrorHandlingHelpers.ValidateFilePath(filePath, logger);
ErrorHandlingHelpers.ValidateFileExists(filePath, logger);

logger.LogInformation("Reading types from document at {FilePath} (stateless)", filePath);

var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);

try {
if (document == null) {
throw new McpException($"Could not load document from file: {filePath}");
}

var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
if (syntaxRoot == null) {
throw new McpException($"Could not parse syntax tree for document: {filePath}");
}

var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
if (semanticModel == null) {
throw new McpException($"Could not get semantic model for document: {filePath}");
}

var typeNodes = syntaxRoot.DescendantNodes()
.OfType<TypeDeclarationSyntax>();

var result = new List<object>();
foreach (var typeNode in typeNodes) {
// Process only top-level types. Nested types are handled by BuildRoslynSubtypeTreeAsync.
if (typeNode.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax) {
var declaredSymbol = semanticModel.GetDeclaredSymbol(typeNode, cancellationToken);

if (declaredSymbol is INamedTypeSymbol namedTypeSymbol) {
// Ensure the symbol is not an error type and has proper context
if (namedTypeSymbol.TypeKind != TypeKind.Error) {
result.Add(await AnalysisTools.BuildRoslynSubtypeTreeAsync(namedTypeSymbol, codeAnalysisService, cancellationToken));
} else {
logger.LogWarning("Skipping error type symbol {SymbolName} from file {FilePath}",
typeNode.Identifier.Text, filePath);
}
}
}
}

return ToolHelpers.ToJson(new {
file = filePath,
note = $"Use {ToolHelpers.SharpToolPrefix}GetMembers to explore the members of these types. For basic file operations, use filesystem tools.",
types = result
});
} finally {
workspace?.Dispose();
}
}, logger, nameof(ReadTypesFromRoslynDocument), cancellationToken);
}
}