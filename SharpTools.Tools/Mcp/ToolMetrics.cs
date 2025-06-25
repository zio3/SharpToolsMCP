using System.Diagnostics;

namespace SharpTools.Tools.Mcp;

/// <summary>
/// ツールの実行統計とパフォーマンス情報を管理
/// </summary>
public static class ToolMetrics
{
    private static readonly Dictionary<string, ToolExecutionStats> _toolStats = new();
    private static readonly object _lock = new();

    /// <summary>
    /// ツールの実行統計
    /// </summary>
    public class ToolExecutionStats
    {
        public string ToolName { get; set; } = string.Empty;
        public int ExecutionCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public long TotalExecutionTimeMs { get; set; }
        public long MinExecutionTimeMs { get; set; } = long.MaxValue;
        public long MaxExecutionTimeMs { get; set; }
        public DateTime LastExecuted { get; set; }
        
        public double SuccessRate => ExecutionCount > 0 ? (double)SuccessCount / ExecutionCount * 100 : 0;
        public double AverageExecutionTimeMs => ExecutionCount > 0 ? (double)TotalExecutionTimeMs / ExecutionCount : 0;
    }

    /// <summary>
    /// ツールの実行を計測
    /// </summary>
    public static async Task<T> MeasureExecutionAsync<T>(string toolName, Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;
        T result = default(T)!;

        try
        {
            result = await operation();
            success = true;
            return result;
        }
        finally
        {
            stopwatch.Stop();
            RecordExecution(toolName, stopwatch.ElapsedMilliseconds, success);
        }
    }

    /// <summary>
    /// 実行結果を記録
    /// </summary>
    private static void RecordExecution(string toolName, long executionTimeMs, bool success)
    {
        lock (_lock)
        {
            if (!_toolStats.TryGetValue(toolName, out var stats))
            {
                stats = new ToolExecutionStats { ToolName = toolName };
                _toolStats[toolName] = stats;
            }

            stats.ExecutionCount++;
            if (success)
                stats.SuccessCount++;
            else
                stats.ErrorCount++;

            stats.TotalExecutionTimeMs += executionTimeMs;
            stats.MinExecutionTimeMs = Math.Min(stats.MinExecutionTimeMs, executionTimeMs);
            stats.MaxExecutionTimeMs = Math.Max(stats.MaxExecutionTimeMs, executionTimeMs);
            stats.LastExecuted = DateTime.Now;
        }
    }

    /// <summary>
    /// 指定されたツールの統計を取得
    /// </summary>
    public static ToolExecutionStats? GetStats(string toolName)
    {
        lock (_lock)
        {
            return _toolStats.TryGetValue(toolName, out var stats) ? stats : null;
        }
    }

    /// <summary>
    /// 全ツールの統計を取得
    /// </summary>
    public static Dictionary<string, ToolExecutionStats> GetAllStats()
    {
        lock (_lock)
        {
            return new Dictionary<string, ToolExecutionStats>(_toolStats);
        }
    }

    /// <summary>
    /// パフォーマンス情報を含む結果メッセージを生成
    /// </summary>
    public static string FormatResultWithMetrics(string baseResult, string toolName, long executionTimeMs)
    {
        var stats = GetStats(toolName);
        if (stats == null)
        {
            return $"{baseResult}\n\n⏱️ 実行時間: {executionTimeMs}ms";
        }

        return $"{baseResult}\n\n📊 パフォーマンス情報:\n" +
               $"• 実行時間: {executionTimeMs}ms\n" +
               $"• 平均実行時間: {stats.AverageExecutionTimeMs:F1}ms\n" +
               $"• 成功率: {stats.SuccessRate:F1}% ({stats.SuccessCount}/{stats.ExecutionCount})";
    }

    /// <summary>
    /// 統計をリセット
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _toolStats.Clear();
        }
    }

    /// <summary>
    /// 最も使用されているツールの統計を取得
    /// </summary>
    public static IEnumerable<ToolExecutionStats> GetTopUsedTools(int count = 5)
    {
        lock (_lock)
        {
            return _toolStats.Values
                .OrderByDescending(s => s.ExecutionCount)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// 最も遅いツールの統計を取得
    /// </summary>
    public static IEnumerable<ToolExecutionStats> GetSlowestTools(int count = 5)
    {
        lock (_lock)
        {
            return _toolStats.Values
                .Where(s => s.ExecutionCount > 0)
                .OrderByDescending(s => s.AverageExecutionTimeMs)
                .Take(count)
                .ToList();
        }
    }
}
