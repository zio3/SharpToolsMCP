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
/// Simple test for newly implemented stateless tools using existing project files
/// </summary>
class SimpleStatelessTest
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Simple Stateless Tools Test ===\n");
        
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
        services.AddSingleton<ICodeModificationService, CodeModificationService>();
        services.AddSingleton<IFuzzyFqnLookupService, FuzzyFqnLookupService>();
        services.AddSingleton<IDocumentOperationsService, DocumentOperationsService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<AnalysisToolsLogCategory>>();
        var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
        var complexityService = serviceProvider.GetRequiredService<IComplexityAnalysisService>();
        var modificationService = serviceProvider.GetRequiredService<ICodeModificationService>();
        var fuzzyLookupService = serviceProvider.GetRequiredService<IFuzzyFqnLookupService>();

        // Use actual paths relative to test directory
        var solutionPath = "../../../SharpTools.sln";
        var projectPath = "../../../SharpTools.Tools/SharpTools.Tools.csproj";
        var testFilePath = "../../../SharpTools.Tools/Services/SolutionManager.cs";
        
        Console.WriteLine($"Testing with solution: {Path.GetFullPath(solutionPath)}");
        Console.WriteLine($"Testing with project: {Path.GetFullPath(projectPath)}");
        Console.WriteLine($"Testing with file: {Path.GetFullPath(testFilePath)}");
        Console.WriteLine();

        // Test 1: AnalyzeComplexity_Stateless
        await TestAnalyzeComplexity(workspaceFactory, complexityService, fuzzyLookupService, logger, projectPath);
        
        // Test 2: ManageUsings_Stateless
        await TestManageUsings(workspaceFactory, modificationService, logger, testFilePath);
        
        // Test 3: ManageAttributes_Stateless
        await TestManageAttributes(workspaceFactory, modificationService, fuzzyLookupService, logger, projectPath);
        
        Console.WriteLine("\n=== All tests completed ===");
    }

    static async Task TestAnalyzeComplexity(
        StatelessWorkspaceFactory workspaceFactory, 
        IComplexityAnalysisService complexityService,
        IFuzzyFqnLookupService fuzzyLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        string projectPath)
    {
        Console.WriteLine("1. Testing AnalyzeComplexity_Stateless...");
        
        try
        {
            // Test project analysis (simplest test)
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
            Console.WriteLine($"   Result contains metrics: {result.Contains("metrics")}");
            Console.WriteLine($"   Result contains recommendations: {result.Contains("recommendations")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestManageUsings(
        StatelessWorkspaceFactory workspaceFactory, 
        ICodeModificationService modificationService,
        ILogger<AnalysisToolsLogCategory> logger,
        string filePath)
    {
        Console.WriteLine("\n2. Testing ManageUsings_Stateless...");
        
        try
        {
            Console.WriteLine($"   Reading usings from: {Path.GetFileName(filePath)}");
            
            var result = await AnalysisTools.ManageUsings_Stateless(
                workspaceFactory,
                modificationService,
                logger,
                filePath,
                "read",
                "None",
                default);
            
            Console.WriteLine($"   ✓ Read operation completed successfully");
            Console.WriteLine($"   Result contains usings: {result.ToString().Contains("usings")}");
            Console.WriteLine($"   Result contains contextInfo: {result.ToString().Contains("contextInfo")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestManageAttributes(
        StatelessWorkspaceFactory workspaceFactory, 
        ICodeModificationService modificationService,
        IFuzzyFqnLookupService fuzzyLookupService,
        ILogger<AnalysisToolsLogCategory> logger,
        string projectPath)
    {
        Console.WriteLine("\n3. Testing ManageAttributes_Stateless...");
        
        try
        {
            // Test with a known class using full qualified name
            var target = "SharpTools.Tools.Services.SolutionManager";
            
            Console.WriteLine($"   Reading attributes from: {target}");
            
            var result = await AnalysisTools.ManageAttributes_Stateless(
                workspaceFactory,
                modificationService,
                fuzzyLookupService,
                logger,
                projectPath,
                "read",
                "None",
                target,
                default);
            
            Console.WriteLine($"   ✓ Read operation completed successfully");
            Console.WriteLine($"   Result contains attributes: {result.ToString().Contains("attributes")}");
            Console.WriteLine($"   Result contains contextInfo: {result.ToString().Contains("contextInfo")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }
}