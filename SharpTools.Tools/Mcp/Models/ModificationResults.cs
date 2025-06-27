using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models;

/// <summary>
/// Base result for modification operations
/// </summary>
public abstract class ModificationResultBase
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("compilationStatus")]
    public DetailedCompilationStatus? CompilationStatus { get; set; }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, GetType(), new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}

/// <summary>
/// Result for OverwriteMember operation
/// </summary>
public class OverwriteMemberResult : ModificationResultBase
{
    [JsonPropertyName("memberName")]
    public string MemberName { get; set; } = "";

    [JsonPropertyName("memberType")]
    public string MemberType { get; set; } = "";

    [JsonPropertyName("targetClass")]
    public string TargetClass { get; set; } = "";

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Result for FindAndReplace operation
/// </summary>
public class FindAndReplaceResult : ModificationResultBase
{
    [JsonPropertyName("replacementCount")]
    public int ReplacementCount { get; set; }

    [JsonPropertyName("affectedMembers")]
    public List<string> AffectedMembers { get; set; } = new();

    [JsonPropertyName("diff")]
    public string? Diff { get; set; }
}

/// <summary>
/// Result for MoveMember operation
/// </summary>
public class MoveMemberResult : ModificationResultBase
{
    [JsonPropertyName("memberName")]
    public string MemberName { get; set; } = "";

    [JsonPropertyName("sourceClass")]
    public string SourceClass { get; set; } = "";

    [JsonPropertyName("targetClass")]
    public string TargetClass { get; set; } = "";

    [JsonPropertyName("targetFile")]
    public string TargetFile { get; set; } = "";

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Result for RenameSymbol operation
/// </summary>
public class RenameSymbolResult : ModificationResultBase
{
    [JsonPropertyName("oldName")]
    public string OldName { get; set; } = "";

    [JsonPropertyName("newName")]
    public string NewName { get; set; } = "";

    [JsonPropertyName("symbolType")]
    public string SymbolType { get; set; } = "";

    [JsonPropertyName("changedFileCount")]
    public int ChangedFileCount { get; set; }

    [JsonPropertyName("changedFiles")]
    public List<string> ChangedFiles { get; set; } = new();

    [JsonPropertyName("totalReferences")]
    public int TotalReferences { get; set; }
}