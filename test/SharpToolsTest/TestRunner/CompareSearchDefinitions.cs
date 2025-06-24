using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SharpTools.Tools.Services;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Interfaces;

/// <summary>
/// Compares performance and results between stateful SearchDefinitions and stateless SearchDefinitions_Stateless
/// </summary>
class CompareSearchDefinitions
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== SearchDefinitions: Stateful vs Stateless Comparison ===\n");
        
        // Register MSBuild
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<StatelessWorkspaceFactory>();
        services.AddSingleton<ISolutionManager, SolutionManager>();
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<AnalysisToolsLogCategory>>();
        var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
        var projectDiscovery = serviceProvider.GetRequiredService<ProjectDiscoveryService>();
        var solutionManager = serviceProvider.GetRequiredService<ISolutionManager>();

        // Test patterns
        var testPatterns = new[]
        {
            ("Simple method search", @"GetMembers"),
            ("Async pattern", @"async.*Task.*<.*>"),
            ("Interface search", @"interface I\w+Service"),
            ("Complex pattern", @"(public|private).*class.*:\s*I"),
        };

        foreach (var (description, pattern) in testPatterns)
        {
            Console.WriteLine($"Testing: {description}");
            Console.WriteLine($"Pattern: {pattern}");
            Console.WriteLine(new string('-', 60));
            
            await ComparePerformance(
                solutionManager, 
                workspaceFactory, 
                projectDiscovery, 
                logger, 
                pattern);
            
            Console.WriteLine();
        }
        
        Console.WriteLine("=== Comparison completed ===");
    }

    static async Task ComparePerformance(
        ISolutionManager solutionManager,
        StatelessWorkspaceFactory workspaceFactory,
        ProjectDiscoveryService projectDiscovery,
        ILogger<AnalysisToolsLogCategory> logger,
        string pattern)
    {
        var solutionPath = @"SharpTools.sln";
        var stopwatch = new Stopwatch();
        
        // Test Stateful version
        Console.WriteLine("1. Stateful SearchDefinitions:");
        try
        {
            // Load solution first
            if (!solutionManager.IsSolutionLoaded)
            {
                Console.WriteLine("   Loading solution...");
                var loadStopwatch = Stopwatch.StartNew();
                await solutionManager.LoadSolutionAsync(solutionPath);
                loadStopwatch.Stop();
                Console.WriteLine($"   Solution loaded in: {loadStopwatch.ElapsedMilliseconds}ms");
            }
            
            stopwatch.Restart();
            var statefulResult = await AnalysisTools.SearchDefinitions(
                solutionManager,
                logger,
                pattern,
                default);
            stopwatch.Stop();
            
            Console.WriteLine($"   ✓ Execution time: {stopwatch.ElapsedMilliseconds}ms");
            DisplayResultSummary(statefulResult, "   ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
        
        // Test Stateless version
        Console.WriteLine("\n2. Stateless SearchDefinitions_Stateless:");
        try
        {
            stopwatch.Restart();
            var statelessResult = await AnalysisTools.SearchDefinitions_Stateless(
                workspaceFactory,
                projectDiscovery,
                logger,
                solutionPath,
                pattern,
                default);
            stopwatch.Stop();
            
            Console.WriteLine($"   ✓ Execution time: {stopwatch.ElapsedMilliseconds}ms");
            DisplayResultSummary(statelessResult, "   ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }
    
    static void DisplayResultSummary(object result, string indent = "")
    {
        var jsonString = result.ToString();
        
        // Extract total matches
        var totalMatch = System.Text.RegularExpressions.Regex.Match(jsonString, @"""totalMatchesFound""\s*:\s*(\d+)");
        if (totalMatch.Success)
        {
            Console.WriteLine($"{indent}Total matches: {totalMatch.Groups[1].Value}");
        }
        
        // Count unique files
        var fileMatches = System.Text.RegularExpressions.Regex.Matches(jsonString, @"""filePath""\s*:\s*""([^""]+)""");
        var uniqueFiles = new System.Collections.Generic.HashSet<string>();
        foreach (System.Text.RegularExpressions.Match match in fileMatches)
        {
            uniqueFiles.Add(match.Groups[1].Value);
        }
        Console.WriteLine($"{indent}Files with matches: {uniqueFiles.Count}");
        
        // Check for limitations
        if (jsonString.Contains("resultsLimitMessage"))
        {
            Console.WriteLine($"{indent}Note: Results were limited");
        }
        
        if (jsonString.Contains("reflectionLimited"))
        {
            Console.WriteLine($"{indent}Note: Reflection search was limited");
        }
        
        // Show context info for stateless
        if (jsonString.Contains("contextInfo"))
        {
            var projectsMatch = System.Text.RegularExpressions.Regex.Match(jsonString, @"""projectsSearched""\s*:\s*(\d+)");
            if (projectsMatch.Success)
            {
                Console.WriteLine($"{indent}Projects searched: {projectsMatch.Groups[1].Value}");
            }
        }
    }
}