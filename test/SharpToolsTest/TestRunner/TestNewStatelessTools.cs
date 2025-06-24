using System;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SharpTools.Tools.Services;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Interfaces;

/// <summary>
/// Tests for newly implemented stateless tools: AnalyzeComplexity_Stateless, ManageUsings_Stateless, ManageAttributes_Stateless
/// </summary>
class TestNewStatelessTools
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing Newly Implemented Stateless Tools ===\n");
        
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
        services.AddSingleton<ISolutionManager, SolutionManager>(); // Required for ComplexityAnalysisService
        services.AddSingleton<IComplexityAnalysisService, ComplexityAnalysisService>();
        services.AddSingleton<ICodeModificationService, CodeModificationService>();
        services.AddSingleton<IFuzzyFqnLookupService, FuzzyFqnLookupService>();
        services.AddSingleton<IDocumentOperationsService, DocumentOperationsService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<AnalysisToolsLogCategory>>();
        var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
        var complexityService = serviceProvider.GetRequiredService<IComplexityAnalysisService>();
        var modificationService = serviceProvider.GetRequiredService<ICodeModificationService>();
        var fuzzyLookupService = serviceProvider.GetRequiredService<IFuzzyFqnLookupService>();

        // Test all three new tools
        await TestAnalyzeComplexityStateless(workspaceFactory, complexityService, fuzzyLookupService, logger);
        await TestManageUsingsStateless(workspaceFactory, modificationService, logger);
        await TestManageAttributesStateless(workspaceFactory, modificationService, fuzzyLookupService, logger);
        
        Console.WriteLine("\n=== All tests completed ===");
    }

    static async Task TestAnalyzeComplexityStateless(
        StatelessWorkspaceFactory workspaceFactory, 
        IComplexityAnalysisService complexityService,
        IFuzzyFqnLookupService fuzzyLookupService,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("1. Testing AnalyzeComplexity_Stateless...");
        
        try
        {
            // Test method analysis - use absolute path
            var contextPath = Path.GetFullPath(@"..\..\..\SharpTools.Tools\SharpTools.Tools.csproj");
            var target = "SolutionManager.LoadSolutionAsync";
            
            Console.WriteLine($"   Context: {contextPath}");
            Console.WriteLine($"   Target: {target} (method scope)");
            
            var result = await AnalysisTools.AnalyzeComplexity_Stateless(
                workspaceFactory,
                complexityService,
                fuzzyLookupService,
                logger,
                contextPath,
                "method",
                target,
                default);
            
            Console.WriteLine($"   ✓ Method analysis completed");
            DisplayComplexityResults(result);

            // Test class analysis
            var classTarget = "SolutionManager";
            Console.WriteLine($"\n   Testing class analysis: {classTarget}");
            
            var classResult = await AnalysisTools.AnalyzeComplexity_Stateless(
                workspaceFactory,
                complexityService,
                fuzzyLookupService,
                logger,
                contextPath,
                "class",
                classTarget,
                default);
            
            Console.WriteLine($"   ✓ Class analysis completed");
            DisplayComplexityResults(classResult);

            // Test project analysis
            var projectTarget = "SharpTools.Tools";
            Console.WriteLine($"\n   Testing project analysis: {projectTarget}");
            
            var projectResult = await AnalysisTools.AnalyzeComplexity_Stateless(
                workspaceFactory,
                complexityService,
                fuzzyLookupService,
                logger,
                contextPath,
                "project",
                projectTarget,
                default);
            
            Console.WriteLine($"   ✓ Project analysis completed");
            DisplayComplexityResults(projectResult);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestManageUsingsStateless(
        StatelessWorkspaceFactory workspaceFactory, 
        ICodeModificationService modificationService,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("\n2. Testing ManageUsings_Stateless...");
        
        try
        {
            var testFile = Path.GetFullPath(@"..\..\..\SharpTools.Tools\Services\SolutionManager.cs");
            
            Console.WriteLine($"   Target file: {testFile}");
            
            // Test read operation
            Console.WriteLine("   Testing read operation...");
            var readResult = await AnalysisTools.ManageUsings_Stateless(
                workspaceFactory,
                modificationService,
                logger,
                testFile,
                "read",
                "None",
                default);
            
            Console.WriteLine($"   ✓ Read operation completed");
            DisplayUsingsResults(readResult);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestManageAttributesStateless(
        StatelessWorkspaceFactory workspaceFactory, 
        ICodeModificationService modificationService,
        IFuzzyFqnLookupService fuzzyLookupService,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("\n3. Testing ManageAttributes_Stateless...");
        
        try
        {
            var contextPath = Path.GetFullPath(@"..\..\..\SharpTools.Tools\SharpTools.Tools.csproj");
            var target = "SolutionManager";
            
            Console.WriteLine($"   Context: {contextPath}");
            Console.WriteLine($"   Target: {target}");
            
            // Test read operation
            Console.WriteLine("   Testing read operation...");
            var readResult = await AnalysisTools.ManageAttributes_Stateless(
                workspaceFactory,
                modificationService,
                fuzzyLookupService,
                logger,
                contextPath,
                "read",
                "None",
                target,
                default);
            
            Console.WriteLine($"   ✓ Read operation completed");
            DisplayAttributesResults(readResult);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static void DisplayComplexityResults(string result)
    {
        // Simple parsing for display
        if (result.Contains("metrics"))
        {
            Console.WriteLine($"   Metrics included: Yes");
        }
        
        if (result.Contains("recommendations"))
        {
            var recommendationsMatch = System.Text.RegularExpressions.Regex.Match(result, @"""recommendations""\s*:\s*\[(.*?)\]", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (recommendationsMatch.Success)
            {
                var recommendations = recommendationsMatch.Groups[1].Value.Split(',').Length;
                Console.WriteLine($"   Recommendations found: {recommendations}");
            }
        }
        
        if (result.Contains("contextInfo"))
        {
            Console.WriteLine($"   Context info included: Yes");
        }
    }

    static void DisplayUsingsResults(object result)
    {
        var jsonString = result.ToString();
        
        // Count using statements
        var usingsMatch = System.Text.RegularExpressions.Regex.Match(jsonString, @"""usings""\s*:\s*""([^""]*)""");
        if (usingsMatch.Success)
        {
            var usingLines = usingsMatch.Groups[1].Value.Split(new[] { "\\n" }, StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine($"   Using statements found: {usingLines.Length}");
        }
        
        if (jsonString.Contains("globalUsings"))
        {
            Console.WriteLine($"   Global usings checked: Yes");
        }
        
        if (jsonString.Contains("contextInfo"))
        {
            Console.WriteLine($"   Context info included: Yes");
        }
    }

    static void DisplayAttributesResults(object result)
    {
        var jsonString = result.ToString();
        
        if (jsonString.Contains("attributes"))
        {
            var attributesMatch = System.Text.RegularExpressions.Regex.Match(jsonString, @"""attributes""\s*:\s*""([^""]*)""");
            if (attributesMatch.Success)
            {
                var attributes = attributesMatch.Groups[1].Value;
                if (attributes.Contains("No attributes found"))
                {
                    Console.WriteLine($"   Attributes found: None");
                }
                else
                {
                    var attrLines = attributes.Split(new[] { "\\n" }, StringSplitOptions.RemoveEmptyEntries);
                    Console.WriteLine($"   Attributes found: {attrLines.Length}");
                }
            }
        }
        
        if (jsonString.Contains("contextInfo"))
        {
            Console.WriteLine($"   Context info included: Yes");
        }
    }
}