using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Result for GetCompilationDiagnostics operation
/// </summary>
public class CompilationDiagnosticsResult
{
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("diagnostics")]
    public List<DiagnosticDetail> Diagnostics { get; set; } = new();

    [JsonPropertyName("summary")]
    public DiagnosticsSummary Summary { get; set; } = new();

    [JsonPropertyName("elapsedMilliseconds")]
    public long ElapsedMilliseconds { get; set; }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}

/// <summary>
/// Summary of diagnostics
/// </summary>
public class DiagnosticsSummary
{
    [JsonPropertyName("totalDiagnostics")]
    public int TotalDiagnostics { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("affectedFileCount")]
    public int AffectedFileCount { get; set; }

    [JsonPropertyName("affectedFiles")]
    public List<string> AffectedFiles { get; set; } = new();
}

/// <summary>
/// Simple compilation status for existing operations
/// </summary>
public class CompilationStatus
{
    [JsonPropertyName("hasErrors")]
    public bool HasErrors { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = $"詳細は {ToolHelpers.SharpToolPrefix}GetCompilationDiagnostics で確認してください";

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}