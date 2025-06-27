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
            throw new McpException($"操作がキャンセルされました: '{operationName}'\n原因: タイムアウトまたは中断");
        } catch (McpException ex) {
            logger.LogError("McpException in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw;
        } catch (ArgumentException ex) {
            logger.LogError(ex, "Invalid argument in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            var paramName = ex.ParamName ?? "unknown";
            throw new McpException($"引数エラー: '{paramName}' が不正です\n詳細: {ex.Message}\n操作: {operationName}\n必須パラメータを確認してください");
        } catch (InvalidOperationException ex) {
            logger.LogError(ex, "Invalid operation in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"操作エラー: '{operationName}' が失敗しました\n詳細: {ex.Message}\n対象が無効な状態の可能性があります");
        } catch (FileNotFoundException ex) {
            logger.LogError(ex, "File not found in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"ファイルが見つかりません: {ex.Message}\n操作: '{operationName}'\n対処法:\n• パスが正しいか確認（絶対パス推奨）\n• ファイルが移動・削除されていないか確認");
        } catch (IOException ex) {
            logger.LogError(ex, "IO error in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"ファイル操作エラー: {ex.Message}\n操作: '{operationName}'\n対処法:\n• ファイルが他のアプリケーションで開かれていないか確認\n• 書き込み権限を確認");
        } catch (UnauthorizedAccessException ex) {
            logger.LogError(ex, "Access denied in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"アクセス拒否: {ex.Message}\n操作: '{operationName}'\n対処法:\n• ファイル・フォルダの権限を確認\n• 読み取り専用でないか確認");
        } catch (Exception ex) {
            logger.LogError(ex, "Unhandled exception in {Operation} ({Caller}): {Message}", operationName, callerName, ex.Message);
            throw new McpException($"予期しないエラー: {ex.Message}\n操作: '{operationName}'\nログを確認してください");
        }
    }

    /// <summary>
    /// Validates that a parameter is not null or whitespace.
    /// </summary>
    public static void ValidateStringParameter(string? value, string paramName, ILogger logger) {
        if (string.IsNullOrWhiteSpace(value)) {
            logger.LogError("Parameter validation failed: {ParamName} is null or empty", paramName);
            throw new McpException($"引数エラー: '{paramName}' は必須です\n提供された値: {(value == null ? "null" : "空文字列")}\n正しい使用例を確認してください");
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
