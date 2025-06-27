using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Structured result for AddMember operation with support for multiple members
/// </summary>
public class AddMemberResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("targetClass")]
    public string TargetClass { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("addedMembers")]
    public List<AddedMember> AddedMembers { get; set; } = new();

    [JsonPropertyName("statistics")]
    public MemberStatistics Statistics { get; set; } = new();

    [JsonPropertyName("insertPosition")]
    public string InsertPosition { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("compilationStatus")]
    public DetailedCompilationStatus? CompilationStatus { get; set; }

    [JsonPropertyName("addedUsings")]
    public List<string> AddedUsings { get; set; } = new();

    [JsonPropertyName("usingConflicts")]
    public List<string> UsingConflicts { get; set; } = new();

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
/// Information about an added member
/// </summary>
public class AddedMember
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // "Method", "Property", "Field", "Event"

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    [JsonPropertyName("accessibility")]
    public string Accessibility { get; set; } = ""; // "Public", "Private", "Protected", "Internal"

    [JsonPropertyName("returnType")]
    public string ReturnType { get; set; } = "";

    [JsonPropertyName("parameters")]
    public List<ParameterDetail> Parameters { get; set; } = new();

    [JsonPropertyName("insertedAtLine")]
    public int InsertedAtLine { get; set; }
}

/// <summary>
/// Parameter information for methods
/// </summary>
public class ParameterDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; set; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Statistics about added members
/// </summary>
public class MemberStatistics
{
    [JsonPropertyName("totalAdded")]
    public int TotalAdded { get; set; }

    [JsonPropertyName("methodCount")]
    public int MethodCount { get; set; }

    [JsonPropertyName("propertyCount")]
    public int PropertyCount { get; set; }

    [JsonPropertyName("fieldCount")]
    public int FieldCount { get; set; }

    [JsonPropertyName("eventCount")]
    public int EventCount { get; set; }
}