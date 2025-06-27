using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

public class MethodImplementationResult
{
    [JsonPropertyName("searchTerm")]
    public string SearchTerm { get; set; } = string.Empty;
    
    [JsonPropertyName("totalMatches")]
    public int TotalMatches { get; set; }
    
    [JsonPropertyName("methods")]
    public List<MethodImplementationDetail> Methods { get; set; } = new();
    
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class MethodImplementationDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
    
    [JsonPropertyName("fullImplementation")]
    public string FullImplementation { get; set; } = string.Empty;
    
    [JsonPropertyName("fullyQualifiedName")]
    public string FullyQualifiedName { get; set; } = string.Empty;
    
    [JsonPropertyName("containingType")]
    public string ContainingType { get; set; } = string.Empty;
    
    [JsonPropertyName("returnType")]
    public string ReturnType { get; set; } = string.Empty;
    
    [JsonPropertyName("parameters")]
    public List<Tools.AnalysisTools.ParameterDetail> Parameters { get; set; } = new();
    
    [JsonPropertyName("location")]
    public Tools.AnalysisTools.LocationInfo? Location { get; set; }
    
    [JsonPropertyName("xmlDocumentation")]
    public string? XmlDocumentation { get; set; }
    
    [JsonPropertyName("modifiers")]
    public List<string> Modifiers { get; set; } = new();
    
    [JsonPropertyName("isOverloaded")]
    public bool IsOverloaded { get; set; }
    
    [JsonPropertyName("isTruncated")]
    public bool IsTruncated { get; set; }
    
    [JsonPropertyName("actualLineCount")]
    public int ActualLineCount { get; set; }
    
    [JsonPropertyName("displayedLineCount")]
    public int DisplayedLineCount { get; set; }
    
    [JsonPropertyName("dependencies")]
    public MethodDependencies? Dependencies { get; set; }
    
    [JsonPropertyName("truncationWarning")]
    public string? TruncationWarning { get; set; }
}

public class MethodDependencies
{
    [JsonPropertyName("calledMethods")]
    public List<string> CalledMethods { get; set; } = new();
    
    [JsonPropertyName("usedFields")]
    public List<string> UsedFields { get; set; } = new();
    
    [JsonPropertyName("usedProperties")]
    public List<string> UsedProperties { get; set; } = new();
    
    [JsonPropertyName("usedTypes")]
    public List<string> UsedTypes { get; set; } = new();
    
    [JsonPropertyName("thrownExceptions")]
    public List<string> ThrownExceptions { get; set; } = new();
}