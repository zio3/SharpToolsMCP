using Microsoft.CodeAnalysis;
using ModelContextProtocol;
using SharpTools.Tools.Services;
using SharpTools.Tools.Mcp;
using SharpTools.Tools.Mcp.Tools;
using System.Security;
using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace SharpTools.Tools.Mcp.Tools;

// Marker class for ILogger<T> category specific to DocumentTools
public class DocumentToolsLogCategory { }

[McpServerToolType]
public static class DocumentTools {

    private static string previousFilePathWarned = string.Empty;

    private static string AddDiffToResult(string resultMessage, string oldContent, string newContent) {
        // Use the centralized diff generation from ContextInjectors
        string diffResult = ContextInjectors.CreateCodeDiff(oldContent, newContent);
        return $"{resultMessage}\n{diffResult}";
    }
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
    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ReadRawFromRoslynDocument), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Reads the content of a file without requiring a pre-loaded solution. Omits indentation to save tokens.")]
    public static async Task<string> ReadRawFromRoslynDocument(
        IDocumentOperationsService documentOperations,
        ILogger<DocumentToolsLogCategory> logger,
        [Description("The absolute path to the file to read.")] string filePath,
        CancellationToken cancellationToken = default) {

        const int LineCountWarningThreshold = 1000;

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateFileExists(filePath, logger);

            logger.LogInformation("Reading document at {FilePath} (stateless)", filePath);

            if (!File.Exists(filePath)) {
                throw new McpException($"File not found: {filePath}");
            }

            try {
                // Direct file I/O for stateless version
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                
                // Omit indentation to save tokens
                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var trimmedLines = lines.Select(line => line.TrimStart());
                var trimmedContent = string.Join(Environment.NewLine, trimmedLines);
                
                var lineCount = lines.Length;

                if (lineCount > LineCountWarningThreshold) {
                    if (previousFilePathWarned != filePath) {
                        previousFilePathWarned = filePath;

                        throw new McpException(
                            $"WARNING: '{filePath}' is very long (over {LineCountWarningThreshold} lines). " +
                            "Consider using more focused tools to accomplish your task, " +
                            "or call this tool again with the same arguments to override this warning.");
                    }
                    previousFilePathWarned = string.Empty;
                    logger.LogInformation("Proceeding with reading large file ({LineCount} lines) after warning acknowledgment", lineCount);
                }

                return trimmedContent;
            } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
                logger.LogError(ex, "File not found: {FilePath}", filePath);
                throw new McpException($"File not found: {filePath}");
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) {
                logger.LogError(ex, "Failed to read file due to access restrictions: {FilePath}", filePath);
                throw new McpException($"Failed to read file due to access restrictions: {ex.Message}");
            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Unexpected error reading file: {FilePath}", filePath);
                throw new McpException($"Failed to read file: {ex.Message}");
            }
        }, logger, nameof(ReadRawFromRoslynDocument), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(CreateRoslynDocument), Idempotent = true, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Creates a new document file with the specified content without requiring a pre-loaded solution. Returns error if the file already exists.")]
    public static async Task<string> CreateRoslynDocument(
        IDocumentOperationsService documentOperations,
        ILogger<DocumentToolsLogCategory> logger,
        [Description("The absolute path where the file should be created.")] string filePath,
        [Description("The content to write to the file. For C#, omit indentation to save tokens. Code will be auto-formatted if possible.")] string content,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(content, "content", logger);
            content = content.TrimBackslash();

            logger.LogInformation("Creating new document at {FilePath} (stateless)", filePath);

            // Check if file exists
            if (documentOperations.FileExists(filePath)) {
                throw new McpException($"File already exists at {filePath}. Use '{ToolHelpers.SharpToolPrefix}{nameof(ReadRawFromRoslynDocument)}' to understand its contents. Then you can use '{ToolHelpers.SharpToolPrefix}{nameof(OverwriteRoslynDocument)}' if you decide to overwrite what exists.");
            }

            try {
                // For stateless version, create directory if needed
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }
                
                // Direct file I/O for stateless version
                await File.WriteAllTextAsync(filePath, content, cancellationToken);

                // Determine if it's a code file
                bool isCodeFile = documentOperations.IsCodeFile(filePath);
                var fileType = isCodeFile ? "code" : "non-code";

                return $"Created {fileType} file: {filePath}";
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) {
                logger.LogError(ex, "Failed to create file due to IO or access restrictions: {FilePath}", filePath);
                throw new McpException($"Failed to create file due to IO or access restrictions: {ex.Message}");
            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Unexpected error creating file: {FilePath}", filePath);
                throw new McpException($"Failed to create file: {ex.Message}");
            }
        }, logger, nameof(CreateRoslynDocument), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(OverwriteRoslynDocument), Idempotent = true, ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description($"Overwrites an existing document file with the specified content without requiring a pre-loaded solution. You must use {ToolHelpers.SharpToolPrefix}{nameof(ReadRawFromRoslynDocument)} first.")]
    public static async Task<string> OverwriteRoslynDocument(
        IDocumentOperationsService documentOperations,
        ILogger<DocumentToolsLogCategory> logger,
        [Description("The absolute path to the file to overwrite.")] string filePath,
        [Description("The content to write to the file. For C#, omit indentation to save tokens. Code will be auto-formatted if possible.")] string content,
        CancellationToken cancellationToken = default) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ErrorHandlingHelpers.ValidateStringParameter(content, "content", logger);
            content = content.TrimBackslash();

            logger.LogInformation("Overwriting document at {FilePath} (stateless)", filePath);

            // Check if file exists
            if (!documentOperations.FileExists(filePath)) {
                throw new McpException($"File does not exist at {filePath}. Use '{ToolHelpers.SharpToolPrefix}{nameof(CreateRoslynDocument)}' to create it first.");
            }

            try {
                // Read old content for diff
                var oldContent = await File.ReadAllTextAsync(filePath, cancellationToken);

                // Direct file I/O for stateless version
                await File.WriteAllTextAsync(filePath, content, cancellationToken);

                // Determine if it's a code file
                bool isCodeFile = documentOperations.IsCodeFile(filePath);
                var fileType = isCodeFile ? "code" : "non-code";

                // Add diff to result
                var resultMessage = $"Overwritten {fileType} file: {filePath}";
                return AddDiffToResult(resultMessage, oldContent, content);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) {
                logger.LogError(ex, "Failed to overwrite file due to IO or access restrictions: {FilePath}", filePath);
                throw new McpException($"Failed to overwrite file due to IO or access restrictions: {ex.Message}");
            } catch (Exception ex) when (!(ex is McpException || ex is OperationCanceledException)) {
                logger.LogError(ex, "Unexpected error overwriting file: {FilePath}", filePath);
                throw new McpException($"Failed to overwrite file: {ex.Message}");
            }
        }, logger, nameof(OverwriteRoslynDocument), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ReadTypesFromRoslynDocument), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns a comprehensive tree of types (classes, interfaces, structs, etc.) and their members from a specified file without requiring a pre-loaded solution.")]
    public static async Task<object> ReadTypesFromRoslynDocument(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeAnalysisService codeAnalysisService,
        ILogger<DocumentToolsLogCategory> logger,
        [Description("The absolute path to the file to analyze.")] string filePath,
        CancellationToken cancellationToken) {
        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
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
                    note = $"Use {ToolHelpers.SharpToolPrefix}GetMembers to explore the members of these types.",
                    types = result
                });
            } finally {
                workspace?.Dispose();
            }
        }, logger, nameof(ReadTypesFromRoslynDocument), cancellationToken);
    }
}