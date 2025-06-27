using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Structured XML documentation information
/// </summary>
public class XmlDocumentation
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("parameters")]
    public List<XmlParameterDoc> Parameters { get; set; } = new();

    [JsonPropertyName("returns")]
    public string? Returns { get; set; }

    [JsonPropertyName("exceptions")]
    public List<XmlExceptionDoc> Exceptions { get; set; } = new();

    [JsonPropertyName("examples")]
    public List<string> Examples { get; set; } = new();

    [JsonPropertyName("seeAlso")]
    public List<string> SeeAlso { get; set; } = new();

    [JsonPropertyName("rawXml")]
    public string? RawXml { get; set; }
}

/// <summary>
/// Parameter documentation
/// </summary>
public class XmlParameterDoc
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Exception documentation
/// </summary>
public class XmlExceptionDoc
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}