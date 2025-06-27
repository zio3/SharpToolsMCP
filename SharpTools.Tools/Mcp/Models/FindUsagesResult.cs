using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Reference location information for FindUsages operation
/// </summary>
public class SymbolReferenceLocation
{
    [JsonPropertyName("symbolName")]
    public string SymbolName { get; set; } = "";

    [JsonPropertyName("symbolKind")]
    public string SymbolKind { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("columnNumber")]
    public int ColumnNumber { get; set; }

    [JsonPropertyName("contextText")]
    public string ContextText { get; set; } = "";
}

/// <summary>
/// File replacement result for ReplaceAcrossFiles operation
/// </summary>
public class FileReplacementResult
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("matchCount")]
    public int MatchCount { get; set; }

    [JsonPropertyName("changes")]
    public List<object> Changes { get; set; } = new();

    [JsonPropertyName("updated")]
    public bool Updated { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Symbol information for FindUsages results
/// </summary>
public class FoundSymbol
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("fullyQualifiedName")]
    public string FullyQualifiedName { get; set; } = "";

    [JsonPropertyName("containingType")]
    public string? ContainingType { get; set; }
}

/// <summary>
/// Location information for symbol usage
/// </summary>
public class UsageLocation
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("column")]
    public int Column { get; set; }

    [JsonPropertyName("context")]
    public string Context { get; set; } = "";

    [JsonPropertyName("symbolKind")]
    public string SymbolKind { get; set; } = "";
}

/// <summary>
/// File usage information
/// </summary>
public class FileUsage
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("referenceCount")]
    public int ReferenceCount { get; set; }

    [JsonPropertyName("locations")]
    public List<UsageLocation> Locations { get; set; } = new();
}

/// <summary>
/// Result of FindUsages operation
/// </summary>
public class FindUsagesResult
{
    [JsonPropertyName("searchTerm")]
    public string SearchTerm { get; set; } = "";

    [JsonPropertyName("symbolsFound")]
    public List<FoundSymbol> SymbolsFound { get; set; } = new();

    [JsonPropertyName("totalReferences")]
    public int TotalReferences { get; set; }

    [JsonPropertyName("references")]
    public List<FileUsage> References { get; set; } = new();

    [JsonPropertyName("summary")]
    public FindUsagesSummary Summary { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { 
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}

/// <summary>
/// Summary information for FindUsages result
/// </summary>
public class FindUsagesSummary
{
    [JsonPropertyName("affectedFileCount")]
    public int AffectedFileCount { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

/// <summary>
/// Result of ReplaceAcrossFiles operation
/// </summary>
public class ReplaceAcrossFilesResult
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    [JsonPropertyName("replacement")]
    public string Replacement { get; set; } = "";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("totalFilesProcessed")]
    public int TotalFilesProcessed { get; set; }

    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }

    [JsonPropertyName("affectedFiles")]
    public List<FileReplacementResult> AffectedFiles { get; set; } = new();

    [JsonPropertyName("summary")]
    public ReplaceAcrossFilesSummary Summary { get; set; } = new();

    [JsonPropertyName("compilationStatus")]
    public CompilationStatusInfo? CompilationStatus { get; set; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}

/// <summary>
/// Summary information for ReplaceAcrossFiles result
/// </summary>
public class ReplaceAcrossFilesSummary
{
    [JsonPropertyName("affectedFileCount")]
    public int AffectedFileCount { get; set; }

    [JsonPropertyName("totalReplacements")]
    public int TotalReplacements { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("filesUpdated")]
    public int FilesUpdated { get; set; }
}

/// <summary>
/// Compilation status information
/// </summary>
public class CompilationStatusInfo
{
    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}