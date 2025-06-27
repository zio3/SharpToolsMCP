using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using SharpTools.Tools.Mcp.Models;

namespace SharpTools.Tools.Mcp.Helpers;

/// <summary>
/// Helper class for diagnostic information collection and comparison
/// </summary>
public static class DiagnosticHelper
{
    /// <summary>
    /// Capture diagnostic information from a compilation
    /// </summary>
    public static async Task<DiagnosticInfo> CaptureDiagnosticsAsync(
        Compilation compilation, 
        string note,
        CancellationToken cancellationToken = default)
    {
        if (compilation == null)
        {
            return new DiagnosticInfo { Note = "コンパイル情報が取得できませんでした" };
        }

        var diagnostics = await Task.Run(() => 
            compilation.GetDiagnostics(cancellationToken), cancellationToken);

        var errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warningCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var hiddenCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Hidden);

        return new DiagnosticInfo
        {
            ErrorCount = errorCount,
            WarningCount = warningCount,
            HiddenCount = hiddenCount,
            Note = note
        };
    }

    /// <summary>
    /// Calculate operation impact by comparing before and after diagnostics
    /// </summary>
    public static OperationImpact CalculateImpact(DiagnosticInfo before, DiagnosticInfo after)
    {
        var errorChange = after.ErrorCount - before.ErrorCount;
        var warningChange = after.WarningCount - before.WarningCount;
        var hiddenChange = after.HiddenCount - before.HiddenCount;

        var (status, note, recommendation) = DetermineStatus(errorChange, warningChange, hiddenChange);

        return new OperationImpact
        {
            ErrorChange = errorChange,
            WarningChange = warningChange,
            HiddenChange = hiddenChange,
            Status = status,
            Note = note,
            Recommendation = recommendation
        };
    }

    /// <summary>
    /// Determine operation status based on diagnostic changes
    /// </summary>
    private static (string status, string note, string recommendation) DetermineStatus(
        int errorChange, 
        int warningChange, 
        int hiddenChange)
    {
        // Error increased - attention required
        if (errorChange > 0)
        {
            return (
                OperationStatus.AttentionRequired,
                $"新規エラーが{errorChange}件発生しました",
                "追加されたコードを確認し、エラーを修正してください"
            );
        }

        // Error decreased - improved
        if (errorChange < 0)
        {
            return (
                OperationStatus.Improved,
                $"エラーが{-errorChange}件減少しました",
                "良い副作用です。継続して開発可能です"
            );
        }

        // No error change, but warnings increased
        if (warningChange > 0)
        {
            return (
                OperationStatus.WarningOnly,
                $"警告が{warningChange}件増加しました",
                "軽微な問題です。継続して開発可能です"
            );
        }

        // Clean - no significant changes
        return (
            OperationStatus.Clean,
            "既存エラーに影響なし",
            "継続して開発可能です"
        );
    }

    /// <summary>
    /// Create a detailed compilation status by comparing before and after
    /// </summary>
    public static async Task<DetailedCompilationStatus> CreateDetailedStatusAsync(
        Compilation beforeCompilation,
        Compilation afterCompilation,
        CancellationToken cancellationToken = default)
    {
        var beforeDiagnostics = await CaptureDiagnosticsAsync(
            beforeCompilation, 
            "操作前から存在していた診断",
            cancellationToken);

        var afterDiagnostics = await CaptureDiagnosticsAsync(
            afterCompilation,
            "操作後の全体状態",
            cancellationToken);

        var impact = CalculateImpact(beforeDiagnostics, afterDiagnostics);

        return new DetailedCompilationStatus
        {
            BeforeOperation = beforeDiagnostics,
            AfterOperation = afterDiagnostics,
            OperationImpact = impact
        };
    }

    /// <summary>
    /// Format diagnostic change for display
    /// </summary>
    public static string FormatChange(int change)
    {
        if (change > 0) return $"+{change}";
        if (change < 0) return change.ToString();
        return "0";
    }
}