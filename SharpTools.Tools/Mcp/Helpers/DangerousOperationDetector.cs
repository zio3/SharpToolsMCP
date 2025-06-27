using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SharpTools.Tools.Mcp.Models;

namespace SharpTools.Tools.Mcp.Helpers
{
    /// <summary>
    /// Detects dangerous operations and patterns
    /// </summary>
    public static class DangerousOperationDetector
    {
        /// <summary>
        /// Dangerous regex patterns that match too broadly
        /// </summary>
        private static readonly HashSet<string> DangerousPatterns = new()
        {
            ".*",      // Matches everything
            ".+",      // Matches everything (at least one char)
            "^.*$",    // Matches entire lines
            "^.+$",    // Matches entire lines (at least one char)
            "[\\s\\S]*", // Matches everything including newlines
            "[\\s\\S]+", // Matches everything including newlines
            "[^]*",    // Matches everything
            "[^]+",    // Matches everything
        };

        /// <summary>
        /// Checks if a regex pattern is dangerous
        /// </summary>
        public static bool IsDangerousPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return false;

            // Check exact dangerous patterns
            if (DangerousPatterns.Contains(pattern))
                return true;

            // Check patterns that are too broad
            try
            {
                var regex = new Regex(pattern);
                
                // Test if pattern matches everything
                var testStrings = new[] { "a", "123", "test", "\n", " ", ".", "@#$%" };
                if (testStrings.All(s => regex.IsMatch(s)))
                    return true;
            }
            catch
            {
                // Invalid regex is not necessarily dangerous
            }

            return false;
        }

        /// <summary>
        /// Evaluates the risk level based on operation impact
        /// </summary>
        public static (string riskLevel, List<string> riskFactors) EvaluateRiskLevel(
            string? pattern,
            int estimatedChanges,
            int affectedFiles,
            bool isDestructive = false)
        {
            var riskFactors = new List<string>();
            
            // Check for universal pattern
            if (!string.IsNullOrEmpty(pattern) && IsDangerousPattern(pattern))
            {
                riskFactors.Add(RiskTypes.UniversalPattern);
            }

            // Check for mass replacement
            if (estimatedChanges > 100)
            {
                riskFactors.Add(RiskTypes.MassReplacement);
            }

            // Check for multi-file impact
            if (affectedFiles > 10)
            {
                riskFactors.Add(RiskTypes.MultiFileImpact);
            }

            // Check for destructive operation
            if (isDestructive)
            {
                riskFactors.Add(RiskTypes.DestructiveOperation);
            }

            // Determine risk level
            if (riskFactors.Contains(RiskTypes.UniversalPattern) && estimatedChanges > 1000)
            {
                return (RiskLevels.Critical, riskFactors);
            }
            
            if (estimatedChanges > 500 || (affectedFiles > 20 && isDestructive))
            {
                return (RiskLevels.High, riskFactors);
            }
            
            if (estimatedChanges > 50 || affectedFiles > 5)
            {
                return (RiskLevels.Medium, riskFactors);
            }

            return (RiskLevels.Low, riskFactors);
        }

        /// <summary>
        /// Creates a dangerous operation result
        /// </summary>
        public static DangerousOperationResult CreateDangerousOperationResult(
            string? pattern,
            int estimatedChanges,
            int affectedFiles,
            bool isDestructive = false)
        {
            var (riskLevel, riskFactors) = EvaluateRiskLevel(pattern, estimatedChanges, affectedFiles, isDestructive);
            
            if (riskLevel == RiskLevels.Low && riskFactors.Count == 0)
            {
                return new DangerousOperationResult
                {
                    DangerousOperationDetected = false,
                    RiskLevel = riskLevel
                };
            }

            var result = new DangerousOperationResult
            {
                DangerousOperationDetected = true,
                RiskLevel = riskLevel,
                Details = new DangerousOperationDetails
                {
                    Pattern = pattern,
                    EstimatedReplacements = estimatedChanges,
                    AffectedFiles = affectedFiles,
                    RiskFactors = riskFactors
                }
            };

            // Set risk type based on primary risk factor
            if (riskFactors.Contains(RiskTypes.UniversalPattern))
            {
                result.RiskType = RiskTypes.UniversalPattern;
            }
            else if (riskFactors.Contains(RiskTypes.MassReplacement))
            {
                result.RiskType = RiskTypes.MassReplacement;
            }
            else if (riskFactors.Contains(RiskTypes.MultiFileImpact))
            {
                result.RiskType = RiskTypes.MultiFileImpact;
            }
            else if (riskFactors.Contains(RiskTypes.DestructiveOperation))
            {
                result.RiskType = RiskTypes.DestructiveOperation;
            }

            // Set message based on risk level
            result.Message = riskLevel switch
            {
                RiskLevels.Critical => $"❌ 極めて危険な操作が検出されました: {estimatedChanges}箇所の変更",
                RiskLevels.High => $"🚨 危険な操作が検出されました: {estimatedChanges}箇所の大量置換",
                RiskLevels.Medium => $"⚠️ 注意: {estimatedChanges}箇所の変更が予定されています",
                _ => $"操作により{estimatedChanges}箇所が変更されます"
            };

            // Set recommendation
            result.Recommendation = riskLevel switch
            {
                RiskLevels.Critical => "必ずdryRunで事前確認し、ユーザーの明示的な許可を得てから実行してください",
                RiskLevels.High => "ユーザーに確認を求めてから実行してください",
                RiskLevels.Medium => "変更内容を確認することを推奨します",
                _ => "操作を実行します"
            };

            if (riskLevel == RiskLevels.High || riskLevel == RiskLevels.Critical)
            {
                result.PreviewCommand = "同じパラメータで dryRun=true を実行することを強く推奨します";
            }

            return result;
        }
    }
}