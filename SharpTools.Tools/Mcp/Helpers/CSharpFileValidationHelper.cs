using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using System.IO;

namespace SharpTools.Tools.Mcp.Helpers;

/// <summary>
/// C#ファイル検証ヘルパークラス
/// LLMが誤って他言語ファイルをSharpToolsで処理することを防ぐ
/// </summary>
public static class CSharpFileValidationHelper
{
    /// <summary>
    /// C#/.NETプロジェクト関連ファイルかつRoslyn解析対象として適切かを検証
    /// </summary>
    /// <param name="filePath">検証対象ファイルパス</param>
    /// <param name="toolName">呼び出し元ツール名</param>
    /// <param name="logger">ログ出力用（オプション）</param>
    public static void ValidateDotNetFileForRoslyn(string filePath, string toolName, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new McpException("ファイルパスが指定されていません");
        }

        var extension = Path.GetExtension(filePath)?.ToLowerInvariant() ?? string.Empty;
        
        if (!IsDotNetFile(filePath))
        {
            var detectedLanguage = DetectLanguageFromExtension(extension);
            var errorMessage = $@"❌ LANGUAGE_MISMATCH: {toolName}は.NET専用ツールです
入力ファイル: {Path.GetFileName(filePath)} ({extension})
検出言語: {detectedLanguage}
対応形式: .NET関連ファイル (.cs, .csx, .sln, .csproj, .vbproj)
SharpToolsは.NET/C#プロジェクト専用の解析ツールです。";

            logger?.LogError("Language mismatch detected: {Language} file provided to {Tool}", detectedLanguage, toolName);
            throw new McpException(errorMessage);
        }

        logger?.LogDebug("File validation passed for {Tool}: {FilePath}", toolName, filePath);
    }

    /// <summary>
    /// ファイル拡張子から言語を検出
    /// </summary>
    /// <param name="extension">ファイル拡張子</param>
    /// <returns>検出された言語名</returns>
    private static string DetectLanguageFromExtension(string extension)
    {
        return extension switch
        {
            ".py" => "Python",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".jsx" => "JavaScript (React)",
            ".tsx" => "TypeScript (React)",
            ".java" => "Java",
            ".cpp" or ".cc" or ".cxx" => "C++",
            ".c" => "C",
            ".h" or ".hpp" => "C/C++ Header",
            ".go" => "Go",
            ".rs" => "Rust",
            ".php" => "PHP",
            ".rb" => "Ruby",
            ".swift" => "Swift",
            ".kt" or ".kts" => "Kotlin",
            ".scala" => "Scala",
            ".r" => "R",
            ".lua" => "Lua",
            ".dart" => "Dart",
            ".pl" => "Perl",
            ".sh" or ".bash" => "Shell Script",
            ".ps1" => "PowerShell",
            ".txt" => "Text",
            ".md" => "Markdown",
            ".json" => "JSON",
            ".xml" => "XML",
            ".yaml" or ".yml" => "YAML",
            ".toml" => "TOML",
            ".ini" => "INI",
            ".pyproj" => "Python Project",
            ".njsproj" => "Node.js Project",
            ".vcxproj" => "C++ Project",
            "" => "拡張子なし",
            _ => "不明な形式"
        };
    }

    /// <summary>
    /// .NET/C#関連ファイル拡張子かどうかの判定
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>Roslyn解析対象ファイルの場合true</returns>
    private static bool IsDotNetFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath)?.ToLowerInvariant() ?? string.Empty;

        return extension switch
        {
            ".cs" => true,     // C# ソースファイル
            ".csx" => true,    // C# スクリプトファイル
            ".sln" => true,    // ソリューションファイル
            ".csproj" => true, // C# プロジェクトファイル
            ".vbproj" => true, // VB.NET プロジェクトファイル
            _ => false
        };
    }
}