using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Services;

class TestExternalSymbol
{
    static async Task Main(string[] args)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpToolsTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var projectPath = Path.Combine(tempDir, "TestProject.csproj");
            var codePath = Path.Combine(tempDir, "TestCode.cs");
            
            // Create project file with logging package reference
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""9.0.0"" />
  </ItemGroup>
</Project>");
            
            // Create code file
            await File.WriteAllTextAsync(codePath, @"using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class TestClass
    {
        private readonly ILogger<TestClass> _logger;
        
        public TestClass(ILogger<TestClass> logger)
        {
            _logger = logger;
        }
        
        public void DoWork()
        {
            _logger.LogInformation(""Working..."");
        }
    }
}");
            
            // Create workspace factory
            var discoveryService = new ProjectDiscoveryService();
            var workspaceFactory = new StatelessWorkspaceFactory(
                NullLogger<StatelessWorkspaceFactory>.Instance, 
                discoveryService);
            
            // Test searching for ILogger
            Console.WriteLine("=== Testing ILogger search ===");
            var result = await AnalysisTools.FindUsages(
                workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "ILogger",
                maxResults: 100,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: true,
                searchMode: "all",
                CancellationToken.None
            );
            
            Console.WriteLine(result.ToString());
            
            // Test searching for _logger
            Console.WriteLine("\n=== Testing _logger search ===");
            result = await AnalysisTools.FindUsages(
                workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "_logger",
                maxResults: 100,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: true,
                searchMode: "all",
                CancellationToken.None
            );
            
            Console.WriteLine(result.ToString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}