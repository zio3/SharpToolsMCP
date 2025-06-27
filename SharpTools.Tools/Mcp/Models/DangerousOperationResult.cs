using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTools.Tools.Mcp.Models
{
    /// <summary>
    /// Result of dangerous operation detection
    /// </summary>
    public class DangerousOperationResult
    {
        [JsonPropertyName("dangerousOperationDetected")]
        public bool DangerousOperationDetected { get; set; }

        [JsonPropertyName("riskLevel")]
        public string RiskLevel { get; set; } = "low";

        [JsonPropertyName("riskType")]
        public string RiskType { get; set; } = "";

        [JsonPropertyName("details")]
        public DangerousOperationDetails Details { get; set; } = new();

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("recommendation")]
        public string Recommendation { get; set; } = "";

        [JsonPropertyName("previewCommand")]
        public string? PreviewCommand { get; set; }

        [JsonPropertyName("requiredConfirmationText")]
        public string? RequiredConfirmationText { get; set; }

        [JsonPropertyName("confirmationPrompt")]
        public string? ConfirmationPrompt { get; set; }

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
    /// Details of dangerous operation
    /// </summary>
    public class DangerousOperationDetails
    {
        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; }

        [JsonPropertyName("estimatedReplacements")]
        public int EstimatedReplacements { get; set; }

        [JsonPropertyName("affectedFiles")]
        public int AffectedFiles { get; set; }

        [JsonPropertyName("riskFactors")]
        public List<string> RiskFactors { get; set; } = new();
    }

    /// <summary>
    /// Risk levels for operations
    /// </summary>
    public static class RiskLevels
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";
        public const string Critical = "critical";
    }

    /// <summary>
    /// Risk types for operations
    /// </summary>
    public static class RiskTypes
    {
        public const string MassReplacement = "mass_replacement";
        public const string UniversalPattern = "universal_pattern";
        public const string MultiFileImpact = "multi_file_impact";
        public const string DestructiveOperation = "destructive_operation";
    }
}