using System;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SharpTools.Tools.Services;
using SharpTools.Tools.Mcp.Tools;

class TestSearchDefinitionsStateless
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== SearchDefinitions_Stateless Test ===");
        
        // Register MSBuild
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<StatelessWorkspaceFactory>();
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<AnalysisToolsLogCategory>>();
        var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
        var projectDiscovery = serviceProvider.GetRequiredService<ProjectDiscoveryService>();

        // Test cases
        await TestFileContext(workspaceFactory, projectDiscovery, logger);
        await TestProjectContext(workspaceFactory, projectDiscovery, logger);
        await TestSolutionContext(workspaceFactory, projectDiscovery, logger);
        await TestPatternSearch(workspaceFactory, projectDiscovery, logger);
        
        Console.WriteLine("\n=== All tests completed ===");
    }

    static async Task TestFileContext(
        StatelessWorkspaceFactory workspaceFactory, 
        ProjectDiscoveryService projectDiscovery,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("\n1. Testing with file context...");
        
        try
        {
            var testFile = @"SharpTools.Tools\Services\SolutionManager.cs";
            var pattern = @"Load.*Solution";
            
            Console.WriteLine($"   Context: {testFile}");
            Console.WriteLine($"   Pattern: {pattern}");
            
            var result = await AnalysisTools.SearchDefinitions_Stateless(
                workspaceFactory,
                projectDiscovery,
                logger,
                testFile,
                pattern,
                default);
            
            Console.WriteLine($"   ✓ Success! Found matches in file context");
            DisplayResults(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestProjectContext(
        StatelessWorkspaceFactory workspaceFactory, 
        ProjectDiscoveryService projectDiscovery,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("\n2. Testing with project context...");
        
        try
        {
            var testProject = @"SharpTools.Tools\SharpTools.Tools.csproj";
            var pattern = @"ILogger.*<";
            
            Console.WriteLine($"   Context: {testProject}");
            Console.WriteLine($"   Pattern: {pattern}");
            
            var result = await AnalysisTools.SearchDefinitions_Stateless(
                workspaceFactory,
                projectDiscovery,
                logger,
                testProject,
                pattern,
                default);
            
            Console.WriteLine($"   ✓ Success! Found matches in project context");
            DisplayResults(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestSolutionContext(
        StatelessWorkspaceFactory workspaceFactory, 
        ProjectDiscoveryService projectDiscovery,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("\n3. Testing with solution context...");
        
        try
        {
            var testSolution = @"SharpTools.sln";
            var pattern = @"McpServerTool";
            
            Console.WriteLine($"   Context: {testSolution}");
            Console.WriteLine($"   Pattern: {pattern}");
            
            var result = await AnalysisTools.SearchDefinitions_Stateless(
                workspaceFactory,
                projectDiscovery,
                logger,
                testSolution,
                pattern,
                default);
            
            Console.WriteLine($"   ✓ Success! Found matches in solution context");
            DisplayResults(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestPatternSearch(
        StatelessWorkspaceFactory workspaceFactory, 
        ProjectDiscoveryService projectDiscovery,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("\n4. Testing complex pattern search...");
        
        try
        {
            var testProject = @"SharpTools.Tools\SharpTools.Tools.csproj";
            var patterns = new[]
            {
                @"async.*Task.*Stateless",     // Methods with async, Task, and Stateless
                @"class.*Service",              // Classes with Service in name
                @"interface I[A-Z]\w+Service"   // Interface pattern
            };
            
            foreach (var pattern in patterns)
            {
                Console.WriteLine($"\n   Testing pattern: {pattern}");
                
                var result = await AnalysisTools.SearchDefinitions_Stateless(
                    workspaceFactory,
                    projectDiscovery,
                    logger,
                    testProject,
                    pattern,
                    default);
                
                DisplayResults(result);
            }
            
            Console.WriteLine($"\n   ✓ All pattern tests completed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static void DisplayResults(object result)
    {
        // Parse the JSON result and display key information
        var jsonString = result.ToString();
        
        // Simple parsing for display (in real test, use proper JSON parsing)
        if (jsonString.Contains("totalMatchesFound"))
        {
            var totalMatch = System.Text.RegularExpressions.Regex.Match(jsonString, @"""totalMatchesFound""\s*:\s*(\d+)");
            if (totalMatch.Success)
            {
                Console.WriteLine($"   Total matches found: {totalMatch.Groups[1].Value}");
            }
        }
        
        if (jsonString.Contains("contextInfo"))
        {
            Console.WriteLine($"   Context info included: Yes");
        }
        
        if (jsonString.Contains("resultsLimitMessage"))
        {
            Console.WriteLine($"   Results limited: Yes");
        }
        
        if (jsonString.Contains("reflectionLimited"))
        {
            Console.WriteLine($"   Reflection search: Limited");
        }
    }
}