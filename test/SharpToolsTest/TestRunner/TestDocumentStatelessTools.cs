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
/// Test for Document-related Stateless tools
/// </summary>
class TestDocumentStatelessTools
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing Document Stateless Tools ===\n");
        
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
        services.AddSingleton<ICodeModificationService, CodeModificationService>(); // For DocumentOperationsService
        services.AddSingleton<ISolutionManager, SolutionManager>(); // For CodeAnalysisService
        services.AddSingleton<IFuzzyFqnLookupService, FuzzyFqnLookupService>(); // For SolutionManager
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<DocumentToolsLogCategory>>();
        var documentOperations = serviceProvider.GetRequiredService<IDocumentOperationsService>();
        var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
        var codeAnalysisService = serviceProvider.GetRequiredService<ICodeAnalysisService>();

        // Use the test project directory for file operations
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var testDir = Path.GetFullPath(Path.Combine(baseDir, "../../../"));
        Console.WriteLine($"Test directory: {testDir}\n");

        try
        {
            // Test 1: ReadRawFromRoslynDocument_Stateless
            await TestReadRaw(documentOperations, logger);
            
            // Test 2: CreateRoslynDocument_Stateless
            var testFile = await TestCreate(documentOperations, logger, testDir);
            
            // Test 3: OverwriteRoslynDocument_Stateless
            await TestOverwrite(documentOperations, logger, testFile);
            
            // Test 4: ReadTypesFromRoslynDocument_Stateless
            await TestReadTypes(workspaceFactory, codeAnalysisService, logger, testFile);
            
            Console.WriteLine("\n=== All tests completed successfully! ===");
        }
        finally
        {
            // Cleanup test files (but not the directory)
            try
            {
                var testFile = Path.Combine(testDir, "TestClass.cs");
                if (File.Exists(testFile))
                {
                    File.Delete(testFile);
                    Console.WriteLine($"\nCleaned up test file");
                }
            }
            catch { }
        }
    }

    static async Task TestReadRaw(
        IDocumentOperationsService documentOperations,
        ILogger<DocumentToolsLogCategory> logger)
    {
        Console.WriteLine("1. Testing ReadRawFromRoslynDocument_Stateless...");
        
        try
        {
            // Read an existing file from the project
            var filePath = Path.GetFullPath("../../../SharpTools.Tools/Services/SolutionManager.cs");
            
            Console.WriteLine($"   Reading file: {Path.GetFileName(filePath)}");
            
            var content = await DocumentTools.ReadRawFromRoslynDocument_Stateless(
                documentOperations,
                logger,
                filePath,
                default);
            
            Console.WriteLine($"   ✓ Read completed successfully");
            Console.WriteLine($"   ✓ Content length: {content.Length} characters");
            Console.WriteLine($"   ✓ Contains 'class SolutionManager': {content.Contains("class SolutionManager")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task<string> TestCreate(
        IDocumentOperationsService documentOperations,
        ILogger<DocumentToolsLogCategory> logger,
        string testDir)
    {
        Console.WriteLine("\n2. Testing CreateRoslynDocument_Stateless...");
        
        var testFile = Path.Combine(testDir, "TestClass.cs");
        
        try
        {
            var content = @"using System;

namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
        
        public void TestMethod()
        {
            Console.WriteLine(""Hello from test!"");
        }
    }
}";
            
            Console.WriteLine($"   Creating file: {Path.GetFileName(testFile)}");
            
            var result = await DocumentTools.CreateRoslynDocument_Stateless(
                documentOperations,
                logger,
                testFile,
                content,
                default);
            
            Console.WriteLine($"   ✓ {result}");
            Console.WriteLine($"   ✓ File exists: {File.Exists(testFile)}");
            
            return testFile;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
            return testFile;
        }
    }

    static async Task TestOverwrite(
        IDocumentOperationsService documentOperations,
        ILogger<DocumentToolsLogCategory> logger,
        string testFile)
    {
        Console.WriteLine("\n3. Testing OverwriteRoslynDocument_Stateless...");
        
        try
        {
            var newContent = @"using System;
using System.Collections.Generic;

namespace TestNamespace
{
    /// <summary>
    /// Updated test class
    /// </summary>
    public class TestClass
    {
        public string Name { get; set; }
        public int Age { get; set; }
        
        public void TestMethod()
        {
            Console.WriteLine($""Hello {Name}, age {Age}!"");
        }
        
        public List<string> GetItems()
        {
            return new List<string> { ""Item1"", ""Item2"" };
        }
    }
}";
            
            Console.WriteLine($"   Overwriting file: {Path.GetFileName(testFile)}");
            
            var result = await DocumentTools.OverwriteRoslynDocument_Stateless(
                documentOperations,
                logger,
                testFile,
                newContent,
                default);
            
            Console.WriteLine($"   ✓ Overwrite completed successfully");
            Console.WriteLine($"   ✓ Result contains diff: {result.Contains("@@")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }

    static async Task TestReadTypes(
        StatelessWorkspaceFactory workspaceFactory,
        ICodeAnalysisService codeAnalysisService,
        ILogger<DocumentToolsLogCategory> logger,
        string testFile)
    {
        Console.WriteLine("\n4. Testing ReadTypesFromRoslynDocument_Stateless...");
        
        try
        {
            Console.WriteLine($"   Reading types from: {Path.GetFileName(testFile)}");
            
            var result = await DocumentTools.ReadTypesFromRoslynDocument_Stateless(
                workspaceFactory,
                codeAnalysisService,
                logger,
                testFile,
                default);
            
            var jsonResult = result.ToString();
            Console.WriteLine($"   ✓ Read types completed successfully");
            Console.WriteLine($"   ✓ Result contains 'TestClass': {jsonResult.Contains("TestClass")}");
            Console.WriteLine($"   ✓ Result contains 'types': {jsonResult.Contains("types")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ Error: {ex.Message}");
        }
    }
}