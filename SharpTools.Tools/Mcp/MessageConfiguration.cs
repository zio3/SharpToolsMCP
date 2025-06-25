using System.Text.Json;

namespace SharpTools.Tools.Mcp;

/// <summary>
/// メッセージングの設定を管理するクラス
/// 将来的にユーザー設定や多言語対応に使用
/// </summary>
public static class MessageConfiguration
{
    /// <summary>
    /// 使用言語（現在は日本語固定、将来的に設定可能）
    /// </summary>
    public static string Language { get; set; } = "ja";
    
    /// <summary>
    /// 絵文字を使用するかどうか
    /// </summary>
    public static bool UseEmojis { get; set; } = true;
    
    /// <summary>
    /// 次のステップ提案を含めるかどうか
    /// </summary>
    public static bool IncludeNextSteps { get; set; } = true;
    
    /// <summary>
    /// エラーメッセージに解決策を含めるかどうか
    /// </summary>
    public static bool IncludeSolutions { get; set; } = true;

    /// <summary>
    /// 成功メッセージを生成
    /// </summary>
    public static string FormatSuccessMessage(string action, string details, params string[] nextSteps)
    {
        var emoji = UseEmojis ? "✅ " : "";
        var message = $"{emoji}{action}";
        
        if (!string.IsNullOrEmpty(details))
        {
            message += $"\n{details}";
        }
        
        if (IncludeNextSteps && nextSteps.Length > 0)
        {
            message += "\n\n💡 次のステップ:";
            foreach (var step in nextSteps)
            {
                message += $"\n• {step}";
            }
        }
        
        return message;
    }
    
    /// <summary>
    /// エラーメッセージを生成
    /// </summary>
    public static string FormatErrorMessage(string problem, string details, params string[] solutions)
    {
        var emoji = UseEmojis ? "❌ " : "";
        var message = $"{emoji}{problem}";
        
        if (!string.IsNullOrEmpty(details))
        {
            message += $": {details}";
        }
        
        if (IncludeSolutions && solutions.Length > 0)
        {
            message += "\n💡 解決策:";
            foreach (var solution in solutions)
            {
                message += $"\n• {solution}";
            }
        }
        
        return message;
    }
    
    /// <summary>
    /// 警告メッセージを生成
    /// </summary>
    public static string FormatWarningMessage(string warning, string details, params string[] recommendations)
    {
        var emoji = UseEmojis ? "⚠️ " : "";
        var message = $"{emoji}{warning}";
        
        if (!string.IsNullOrEmpty(details))
        {
            message += $": {details}";
        }
        
        if (recommendations.Length > 0)
        {
            message += "\n💡 推奨事項:";
            foreach (var rec in recommendations)
            {
                message += $"\n• {rec}";
            }
        }
        
        return message;
    }
    
    /// <summary>
    /// 情報メッセージを生成
    /// </summary>
    public static string FormatInfoMessage(string info, params string[] additionalInfo)
    {
        var emoji = UseEmojis ? "ℹ️ " : "";
        var message = $"{emoji}{info}";
        
        if (additionalInfo.Length > 0)
        {
            foreach (var additional in additionalInfo)
            {
                message += $"\n{additional}";
            }
        }
        
        return message;
    }

    /// <summary>
    /// 設定をJSONから読み込み（将来の機能）
    /// </summary>
    public static void LoadFromJson(string jsonPath)
    {
        if (File.Exists(jsonPath))
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                var config = JsonSerializer.Deserialize<MessageConfig>(json);
                if (config != null)
                {
                    Language = config.Language ?? Language;
                    UseEmojis = config.UseEmojis;
                    IncludeNextSteps = config.IncludeNextSteps;
                    IncludeSolutions = config.IncludeSolutions;
                }
            }
            catch
            {
                // 設定読み込みエラーは無視（デフォルト値を使用）
            }
        }
    }

    private class MessageConfig
    {
        public string? Language { get; set; }
        public bool UseEmojis { get; set; } = true;
        public bool IncludeNextSteps { get; set; } = true;
        public bool IncludeSolutions { get; set; } = true;
    }
}
