using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using ModelContextProtocol;
using SharpTools.Tools.Services;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpTools.Tools.Mcp;

/// <summary>
/// Provides centralized error handling helpers for SharpTools.
/// </summary>
internal static class ErrorHandlingHelpers {
    /// <summary>
    /// Executes a function with comprehensive error handling and logging.
    /// </summary>
    public static async Task<T> ExecuteWithErrorHandlingAsync<T, TLogCategory>(
    Func<Task<T>> operation,
    ILogger<TLogCategory> logger,
    string operationName,
    CancellationToken cancellationToken,
    [CallerMemberName] string callerName = "") {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation();
        } catch (OperationCanceledException) {
            logger.LogWarning("{Operation} operation in {Caller} was cancelled", operationName, callerName);
            throw new McpException($"Operation '{operationName}' was cancelled.\n💡 This usually happens when the operation takes too long or is interrupted.");
        } catch (McpException ex) {
            logger.LogError("McpException in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw;
        } catch (ArgumentException ex) {
            logger.LogError(ex, "Invalid argument in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"❌ Invalid parameter for '{operationName}': {ex.Message}\n💡 Check that all required parameters are provided and correctly formatted.");
        } catch (InvalidOperationException ex) {
            logger.LogError(ex, "Invalid operation in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"❌ Operation '{operationName}' failed: {ex.Message}\n💡 This might happen if the target is in an invalid state. Try refreshing the workspace or checking prerequisites.");
        } catch (FileNotFoundException ex) {
            logger.LogError(ex, "File not found in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"📁 File not found during '{operationName}': {ex.Message}\n💡 Solutions:\n• Verify the file path is correct (use absolute paths for best results)\n• Check if the file was moved or deleted\n• Ensure you have proper file permissions");
        } catch (IOException ex) {
            logger.LogError(ex, "IO error in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"💾 File operation error during '{operationName}': {ex.Message}\n💡 Solutions:\n• Check if the file is open in another application\n• Verify you have write permissions to the directory\n• Ensure there's enough disk space");
        } catch (UnauthorizedAccessException ex) {
            logger.LogError(ex, "Access denied in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"🔒 Access denied during '{operationName}': {ex.Message}\n💡 Solutions:\n• Run with administrator privileges if needed\n• Check file and folder permissions\n• Ensure the file isn't read-only or locked by another process");
        } catch (Exception ex) {
            logger.LogError(ex, "Unhandled exception in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"⚠️ Unexpected error during '{operationName}': {ex.Message}\n💡 This is an unexpected error. Please check the logs for more details and consider reporting this issue.");
        }
    }

    /// <summary>
    /// Validates that a parameter is not null or whitespace.
    /// </summary>
    public static void ValidateStringParameter(string? value, string paramName, ILogger logger) {
        if (string.IsNullOrWhiteSpace(value)) {
            logger.LogError("Parameter validation failed: {ParamName} is null or empty", paramName);
            throw new McpException($"❌ Parameter '{paramName}' is required but was empty.\n💡 Provide a valid value for this parameter. Check the tool description for examples.");
        }
    }

    /// <summary>
    /// Validates that a file path is valid and not empty.
    /// </summary>
    public static void ValidateFilePath(string? filePath, ILogger logger) {
        ValidateStringParameter(filePath, "filePath", logger);

        try {
            // Check if the path is valid
            var fullPath = Path.GetFullPath(filePath!);

            // Additional checks if needed (e.g., file exists, is accessible, etc.)
        } catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException) {
            logger.LogError(ex, "Invalid file path: {FilePath}", filePath);
            throw new McpException($"Invalid file path: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates that a file exists at the specified path.
    /// </summary>
    public static void ValidateFileExists(string? filePath, ILogger logger) {
        ValidateFilePath(filePath, logger);

        if (!File.Exists(filePath)) {
            logger.LogError("File does not exist at path: {FilePath}", filePath);
            throw new McpException($"📁 File not found: {filePath}\n💡 Solutions:\n• Check the file path spelling and format\n• Use absolute paths (e.g., C:\\full\\path\\to\\file.cs)\n• Verify the file wasn't moved or deleted\n• Use SharpTool_SearchDefinitions to find files if unsure of location");
        }
    }
    /// <summary>
    /// Checks for compilation errors in a document after code has been modified.
    /// </summary>
    /// <param name="solutionManager">The solution manager</param>
    /// <param name="document">The document to check for errors</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A tuple containing (hasErrors, errorMessages)</returns>
    public static async Task<(bool HasErrors, string ErrorMessages)> CheckCompilationErrorsAsync<TLogCategory>(
        ISolutionManager solutionManager,
        Document document,
        ILogger<TLogCategory> logger,
        CancellationToken cancellationToken) {

        // Delegate to the centralized implementation in ContextInjectors
        return await ContextInjectors.CheckCompilationErrorsAsync(solutionManager, document, logger, cancellationToken);
    }
}
