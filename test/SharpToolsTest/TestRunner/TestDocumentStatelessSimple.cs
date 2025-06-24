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
/// Simple test for Document-related Stateless tools (focused on read operations)
/// </summary>
class TestDocumentStatelessSimple
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing Document Stateless Tools (Simple) ===\n");
        
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
        services.AddSingleton<IDocumentOperationsService, DocumentOperationsService>();
        services.AddSingleton<ICodeAnalysisService, CodeAnalysisService>();
        services.AddSingleton<ICodeModificationService, CodeModificationService>();
        services.AddSingleton<ISolutionManager, SolutionManager>();
        services.AddSingleton<IFuzzyFqnLookupService, FuzzyFqnLookupService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<DocumentToolsLogCategory>>();
        var documentOperations = serviceProvider.GetRequiredService<IDocumentOperationsService>();
        var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
        var codeAnalysisService = serviceProvider.GetRequiredService<ICodeAnalysisService>();

        // Test 1: ReadRawFromRoslynDocument_Stateless with actual project file
        await TestReadRawWithProjectFile(documentOperations, logger);
        
        // Test 2: ReadTypesFromRoslynDocument_Stateless with actual project file
        await TestReadTypesWithProjectFile(workspaceFactory, codeAnalysisService, logger);
        
        Console.WriteLine("\n=== All tests completed! ===");
    }

    static async Task TestReadRawWithProjectFile(
        IDocumentOperationsService documentOperations,
        ILogger<DocumentToolsLogCategory> logger)
    {
        Console.WriteLine("1. Testing ReadRawFromRoslynDocument_Stateless...");
        
        try
        {
            // Use Program.cs from the TestRunner project itself
            var filePath = Path.GetFullPath("Program.cs");
            
            Console.WriteLine($"   Reading file: {filePath}");
            Console.WriteLine($"   File exists: {File.Exists(filePath)}");
            
            var content = await DocumentTools.ReadRawFromRoslynDocument_Stateless(
                documentOperations,
                logger,
                filePath,
                default);
            
            Console.WriteLine($"   ✓ Read completed successfully");
            Console.WriteLine($"   ✓ Content length: {content.Length} characters");
            Console.WriteLine($"   ✓ Content starts with 'using': {content.TrimStart().StartsWith("using")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestReadTypesWithProjectFile(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeAnalysisService codeAnalysisService,
        ILogger<DocumentToolsLogCategory> logger)
    {
        Console.WriteLine("\n2. Testing ReadTypesFromRoslynDocument_Stateless...");
        
        try
        {
            // Use a simple test file
            var filePath = Path.GetFullPath("TestSimpleStandalone.cs");
            
            Console.WriteLine($"   Reading types from: {filePath}");
            Console.WriteLine($"   File exists: {File.Exists(filePath)}");
            
            var result = await DocumentTools.ReadTypesFromRoslynDocument_Stateless(
                workspaceFactory,
                codeAnalysisService,
                logger,
                filePath,
                default);
            
            var jsonResult = result.ToString();
            Console.WriteLine($"   ✓ Read types completed successfully");
            Console.WriteLine($"   ✓ Result contains 'types': {jsonResult.Contains("\"types\"")}");
            Console.WriteLine($"   ✓ Result length: {jsonResult.Length} characters");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }
}