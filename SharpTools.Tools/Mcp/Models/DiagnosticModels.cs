using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Compilation status with before/after comparison
/// </summary>
public class DetailedCompilationStatus
{
    [JsonPropertyName("beforeOperation")]
    public DiagnosticInfo BeforeOperation { get; set; } = new();

    [JsonPropertyName("afterOperation")]
    public DiagnosticInfo AfterOperation { get; set; } = new();

    [JsonPropertyName("operationImpact")]
    public OperationImpact OperationImpact { get; set; } = new();
}

/// <summary>
/// Diagnostic information snapshot
/// </summary>
public class DiagnosticInfo
{
    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("hiddenCount")]
    public int HiddenCount { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";
}

/// <summary>
/// Operation impact analysis
/// </summary>
public class OperationImpact
{
    [JsonPropertyName("errorChange")]
    public int ErrorChange { get; set; }

    [JsonPropertyName("warningChange")]
    public int WarningChange { get; set; }

    [JsonPropertyName("hiddenChange")]
    public int HiddenChange { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "clean"; // "clean", "improved", "attention_required", "warning_only"

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("recommendation")]
    public string Recommendation { get; set; } = "";
}

/// <summary>
/// Status types for operation impact
/// </summary>
public static class OperationStatus
{
    public const string Clean = "clean";
    public const string Improved = "improved";
    public const string AttentionRequired = "attention_required";
    public const string WarningOnly = "warning_only";
}