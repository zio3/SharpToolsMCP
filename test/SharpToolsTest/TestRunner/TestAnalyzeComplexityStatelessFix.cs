using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SharpTools.Tools.Services;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Interfaces;

/// <summary>
/// Test for verifying AnalyzeComplexity_Stateless fix
/// </summary>
class TestAnalyzeComplexityStatelessFix
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing AnalyzeComplexity_Stateless Fix ===\n");
        
        // Register MSBuild
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<StatelessWorkspaceFactory>();
        services.AddSingleton<ISolutionManager, SolutionManager>();
        services.AddSingleton<IComplexityAnalysisService, ComplexityAnalysisService>();
        services.AddSingleton<IFuzzyFqnLookupService, FuzzyFqnLookupService>();
        services.AddSingleton<IDocumentOperationsService, DocumentOperationsService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<AnalysisToolsLogCategory>>();
        var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
        var complexityService = serviceProvider.GetRequiredService<IComplexityAnalysisService>();
        var fuzzyLookupService = serviceProvider.GetRequiredService<IFuzzyFqnLookupService>();

        var projectPath = Path.GetFullPath("../../../SharpTools.Tools/SharpTools.Tools.csproj");
        
        // Test 1: Method-level analysis
        await TestMethodAnalysis(workspaceFactory, complexityService, fuzzyLookupService, logger, projectPath);
        
        // Test 2: Class-level analysis
        await TestClassAnalysis(workspaceFactory, complexityService, fuzzyLookupService, logger, projectPath);
        
        // Test 3: Project-level analysis
        await TestProjectAnalysis(workspaceFactory, complexityService, fuzzyLookupService, logger, projectPath);
        
        Console.WriteLine("\n=== All tests completed successfully! ===");
    }

    static async Task TestMethodAnalysis(
        StatelessWorkspaceFactory workspaceFactory, 
        IComplexityAnalysisService complexityService,
        IFuzzyFqnLookupService fuzzyLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        string projectPath)
    {
        Console.WriteLine("1. Testing Method-level analysis...");
        
        try
        {
            var target = "SharpTools.Tools.Services.SolutionManager.LoadSolutionAsync";
            
            Console.WriteLine($"   Analyzing method: {target}");
            
            var result = await AnalysisTools.AnalyzeComplexity_Stateless(
                workspaceFactory,
                complexityService,
                fuzzyLookupService,
                logger,
                projectPath,
                "method",
                target,
                default);
            
            Console.WriteLine($"   ✓ Method analysis completed successfully");
            
            // Parse result to check metrics
            if (result.Contains("cyclomaticComplexity"))
            {
                Console.WriteLine($"   ✓ Cyclomatic complexity calculated");
            }
            if (result.Contains("cognitiveComplexity"))
            {
                Console.WriteLine($"   ✓ Cognitive complexity calculated");
            }
            if (result.Contains("lineCount"))
            {
                Console.WriteLine($"   ✓ Line count calculated");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
            if (ex.Message.Contains("SyntaxTree"))
            {
                Console.WriteLine("   >>> SyntaxTree error still present!");
            }
        }
    }

    static async Task TestClassAnalysis(
        StatelessWorkspaceFactory workspaceFactory, 
        IComplexityAnalysisService complexityService,
        IFuzzyFqnLookupService fuzzyLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        string projectPath)
    {
        Console.WriteLine("\n2. Testing Class-level analysis...");
        
        try
        {
            var target = "SharpTools.Tools.Services.SolutionManager";
            
            Console.WriteLine($"   Analyzing class: {target}");
            
            var result = await AnalysisTools.AnalyzeComplexity_Stateless(
                workspaceFactory,
                complexityService,
                fuzzyLookupService,
                logger,
                projectPath,
                "class",
                target,
                default);
            
            Console.WriteLine($"   ✓ Class analysis completed successfully");
            
            if (result.Contains("totalMethods"))
            {
                Console.WriteLine($"   ✓ Method count calculated");
            }
            if (result.Contains("inheritanceDepth"))
            {
                Console.WriteLine($"   ✓ Inheritance depth calculated");
            }
            if (result.Contains("couplingBetweenObjects"))
            {
                Console.WriteLine($"   ✓ Coupling calculated");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
            if (ex.Message.Contains("SyntaxTree"))
            {
                Console.WriteLine("   >>> SyntaxTree error still present!");
            }
        }
    }

    static async Task TestProjectAnalysis(
        StatelessWorkspaceFactory workspaceFactory, 
        IComplexityAnalysisService complexityService,
        IFuzzyFqnLookupService fuzzyLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        string projectPath)
    {
        Console.WriteLine("\n3. Testing Project-level analysis...");
        
        try
        {
            var target = "SharpTools.Tools";
            
            Console.WriteLine($"   Analyzing project: {target}");
            
            var result = await AnalysisTools.AnalyzeComplexity_Stateless(
                workspaceFactory,
                complexityService,
                fuzzyLookupService,
                logger,
                projectPath,
                "project",
                target,
                default);
            
            Console.WriteLine($"   ✓ Project analysis completed successfully");
            
            if (result.Contains("totalTypes"))
            {
                Console.WriteLine($"   ✓ Type count calculated");
            }
            if (result.Contains("totalMethods"))
            {
                Console.WriteLine($"   ✓ Method count calculated");
            }
            if (result.Contains("averageCyclomaticComplexity"))
            {
                Console.WriteLine($"   ✓ Average complexity calculated");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }
}