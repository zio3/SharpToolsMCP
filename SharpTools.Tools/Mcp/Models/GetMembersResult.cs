using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Structured result for GetMembers operation
/// </summary>
public class GetMembersResult
{
    [JsonPropertyName("className")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("fullyQualifiedName")]
    public string FullyQualifiedName { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("baseTypes")]
    public List<string> BaseTypes { get; set; } = new();

    [JsonPropertyName("interfaces")]
    public List<string> Interfaces { get; set; } = new();

    [JsonPropertyName("isPartial")]
    public bool IsPartial { get; set; }

    [JsonPropertyName("members")]
    public List<MemberDetail> Members { get; set; } = new();

    [JsonPropertyName("statistics")]
    public ClassStatistics Statistics { get; set; } = new();

    [JsonPropertyName("nestedTypes")]
    public List<string> NestedTypes { get; set; } = new();

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
/// Information about a class member
/// </summary>
public class MemberDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    [JsonPropertyName("fullyQualifiedName")]
    public string FullyQualifiedName { get; set; } = "";

    [JsonPropertyName("accessibility")]
    public string Accessibility { get; set; } = "";

    [JsonPropertyName("modifiers")]
    public List<string> Modifiers { get; set; } = new();

    [JsonPropertyName("returnType")]
    public string? ReturnType { get; set; }

    [JsonPropertyName("parameters")]
    public List<ParameterInfo>? Parameters { get; set; }

    [JsonPropertyName("location")]
    public MemberLocation Location { get; set; } = new();

    [JsonPropertyName("xmlDocs")]
    public string? XmlDocs { get; set; }

    [JsonPropertyName("attributes")]
    public List<string> Attributes { get; set; } = new();
}

/// <summary>
/// Parameter information
/// </summary>
public class ParameterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("modifiers")]
    public List<string> Modifiers { get; set; } = new();
}

/// <summary>
/// Location information
/// </summary>
public class MemberLocation
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("column")]
    public int Column { get; set; }
}

/// <summary>
/// Statistics about a class
/// </summary>
public class ClassStatistics
{
    [JsonPropertyName("totalMembers")]
    public int TotalMembers { get; set; }

    [JsonPropertyName("publicMembers")]
    public int PublicMembers { get; set; }

    [JsonPropertyName("privateMembers")]
    public int PrivateMembers { get; set; }

    [JsonPropertyName("protectedMembers")]
    public int ProtectedMembers { get; set; }

    [JsonPropertyName("internalMembers")]
    public int InternalMembers { get; set; }

    [JsonPropertyName("methodCount")]
    public int MethodCount { get; set; }

    [JsonPropertyName("propertyCount")]
    public int PropertyCount { get; set; }

    [JsonPropertyName("fieldCount")]
    public int FieldCount { get; set; }

    [JsonPropertyName("eventCount")]
    public int EventCount { get; set; }

    [JsonPropertyName("nestedTypeCount")]
    public int NestedTypeCount { get; set; }
}