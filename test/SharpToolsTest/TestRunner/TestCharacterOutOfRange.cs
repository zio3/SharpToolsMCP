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
/// Test for reproducing "character out of range" errors
/// </summary>
class TestCharacterOutOfRange
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing Character Out of Range Error ===\n");
        
        // Register MSBuild
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<ISolutionManager, SolutionManager>();
        services.AddSingleton<ICodeModificationService, CodeModificationService>();
        services.AddSingleton<ICodeAnalysisService, CodeAnalysisService>();
        services.AddSingleton<IDocumentOperationsService, DocumentOperationsService>();
        services.AddSingleton<ISourceResolutionService, SourceResolutionService>();
        services.AddSingleton<IFuzzyFqnLookupService, FuzzyFqnLookupService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var solutionManager = serviceProvider.GetRequiredService<ISolutionManager>();
        var modificationService = serviceProvider.GetRequiredService<ICodeModificationService>();
        var codeAnalysisService = serviceProvider.GetRequiredService<ICodeAnalysisService>();
        var documentOperations = serviceProvider.GetRequiredService<IDocumentOperationsService>();
        var sourceResolutionService = serviceProvider.GetRequiredService<ISourceResolutionService>();
        var logger = serviceProvider.GetRequiredService<ILogger<AnalysisToolsLogCategory>>();
        var docLogger = serviceProvider.GetRequiredService<ILogger<DocumentToolsLogCategory>>();

        // Load solution
        var solutionPath = Path.GetFullPath("../../../SharpTools.sln");
        Console.WriteLine($"Loading solution: {solutionPath}");
        await solutionManager.LoadSolutionAsync(solutionPath, default);
        Console.WriteLine("Solution loaded\n");

        // Test 1: ManageAttributes
        await TestManageAttributes(solutionManager, modificationService, logger);
        
        // Test 2: ViewDefinition
        await TestViewDefinition(solutionManager, logger, codeAnalysisService, sourceResolutionService);
        
        // Test 3: ReadTypesFromRoslynDocument
        await TestReadTypesFromRoslynDocument(solutionManager, documentOperations, codeAnalysisService, docLogger);
        
        Console.WriteLine("\n=== All tests completed ===");
    }

    static async Task TestManageAttributes(
        ISolutionManager solutionManager, 
        ICodeModificationService modificationService,
        ILogger<AnalysisToolsLogCategory> logger)
    {
        Console.WriteLine("1. Testing ManageAttributes...");
        
        try
        {
            // Test with a known class
            var target = "SharpTools.Tools.Services.SolutionManager";
            
            Console.WriteLine($"   Reading attributes from: {target}");
            
            var result = await AnalysisTools.ManageAttributes(
                solutionManager,
                modificationService,
                logger,
                "read",
                "None",
                target,
                default);
            
            Console.WriteLine($"   ✓ Read operation completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
            if (ex.Message.Contains("character"))
            {
                Console.WriteLine("   >>> Character out of range error detected!");
            }
        }
    }

    static async Task TestViewDefinition(
        ISolutionManager solutionManager,
        ILogger<AnalysisToolsLogCategory> logger,
        ICodeAnalysisService codeAnalysisService,
        ISourceResolutionService sourceResolutionService)
    {
        Console.WriteLine("\n2. Testing ViewDefinition...");
        
        try
        {
            var target = "SharpTools.Tools.Services.SolutionManager.LoadSolutionAsync";
            
            Console.WriteLine($"   Viewing definition of: {target}");
            
            var result = await AnalysisTools.ViewDefinition(
                solutionManager,
                logger,
                codeAnalysisService,
                sourceResolutionService,
                target,
                default);
            
            Console.WriteLine($"   ✓ View operation completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
            if (ex.Message.Contains("character"))
            {
                Console.WriteLine("   >>> Character out of range error detected!");
            }
        }
    }

    static async Task TestReadTypesFromRoslynDocument(
        ISolutionManager solutionManager,
        IDocumentOperationsService documentOperations,
        ICodeAnalysisService codeAnalysisService,
        ILogger<DocumentToolsLogCategory> logger)
    {
        Console.WriteLine("\n3. Testing ReadTypesFromRoslynDocument...");
        
        try
        {
            var filePath = Path.GetFullPath("../../../SharpTools.Tools/Services/SolutionManager.cs");
            
            Console.WriteLine($"   Reading types from: {Path.GetFileName(filePath)}");
            
            var result = await DocumentTools.ReadTypesFromRoslynDocument(
                solutionManager,
                documentOperations,
                codeAnalysisService,
                logger,
                filePath,
                default);
            
            Console.WriteLine($"   ✓ Read operation completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
            if (ex.Message.Contains("character"))
            {
                Console.WriteLine("   >>> Character out of range error detected!");
            }
        }
    }
}