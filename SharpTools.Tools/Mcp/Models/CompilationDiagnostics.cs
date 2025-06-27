using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Represents structured compilation diagnostics for better LLM understanding
/// </summary>
public class CompilationDiagnostics
{
    [JsonPropertyName("hasErrors")]
    public bool HasErrors { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("diagnostics")]
    public List<DiagnosticDetail> Diagnostics { get; set; } = new();

    [JsonPropertyName("suggestedActions")]
    public List<string> SuggestedActions { get; set; } = new();

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
/// Detailed information about a specific diagnostic
/// </summary>
public class DiagnosticDetail
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("location")]
    public DiagnosticLocation Location { get; set; } = new();

    [JsonPropertyName("canAutoFix")]
    public bool CanAutoFix { get; set; }

    [JsonPropertyName("suggestedActions")]
    public List<string> SuggestedActions { get; set; } = new();

    [JsonPropertyName("relatedLocations")]
    public List<DiagnosticLocation> RelatedLocations { get; set; } = new();
}

/// <summary>
/// Location information for a diagnostic
/// </summary>
public class DiagnosticLocation
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("column")]
    public int Column { get; set; }

    [JsonPropertyName("endLine")]
    public int? EndLine { get; set; }

    [JsonPropertyName("endColumn")]
    public int? EndColumn { get; set; }

    [JsonPropertyName("codeSnippet")]
    public string? CodeSnippet { get; set; }
}