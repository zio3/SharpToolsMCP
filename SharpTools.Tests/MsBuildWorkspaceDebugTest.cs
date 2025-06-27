using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpTools.Tools.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharpTools.Tests
{
    /// <summary>
    /// "No file format header found" エラーの原因を調査するためのデバッグテスト
    /// このエラーはWindows環境でも発生することが確認されている
    /// </summary>
    [TestClass]
    public class MsBuildWorkspaceDebugTest
    {
        private string _tempDirectory;

        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsDebug_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // ベストエフォート
            }
        }

        [TestMethod]
        public async Task DebugMSBuildWorkspace_SimpleProject_WithDetailedLogging()
        {
            // Arrange
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            var codePath = Path.Combine(_tempDirectory, "TestCode.cs");

            // シンプルなプロジェクトファイルを作成
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";

            await File.WriteAllTextAsync(projectPath, projectContent);

            // シンプルなコードファイルを作成
            var codeContent = @"namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            System.Console.WriteLine(""Test"");
        }
    }
}";

            await File.WriteAllTextAsync(codePath, codeContent);

            Console.WriteLine($"=== MSBuildWorkspace Debug Test ===");
            Console.WriteLine($"Project Path: {projectPath}");
            Console.WriteLine($"Project Exists: {File.Exists(projectPath)}");
            Console.WriteLine($"Code Path: {codePath}");
            Console.WriteLine($"Code Exists: {File.Exists(codePath)}");

            // プロジェクトファイルの内容を確認
            Console.WriteLine("\n=== Project File Content ===");
            Console.WriteLine(await File.ReadAllTextAsync(projectPath));

            // MSBuildLocatorの状態を確認
            Console.WriteLine("\n=== MSBuildLocator Status ===");
            Console.WriteLine($"IsRegistered: {MSBuildLocator.IsRegistered}");
            
            if (MSBuildLocator.IsRegistered)
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances();
                Console.WriteLine($"MSBuild Instances Count: {instances.Count()}");
                foreach (var instance in instances)
                {
                    Console.WriteLine($"  - {instance.Name}: {instance.MSBuildPath}");
                }
            }

            // Act & Assert
            var workspace = MSBuildWorkspace.Create();
            var diagnostics = new List<string>();

            workspace.WorkspaceFailed += (sender, args) =>
            {
                var message = $"[{args.Diagnostic.Kind}] {args.Diagnostic.Message}";
                diagnostics.Add(message);
                Console.WriteLine($"Workspace Diagnostic: {message}");
            };

            try
            {
                Console.WriteLine("\n=== Opening Project ===");
                var project = await workspace.OpenProjectAsync(projectPath);
                
                Console.WriteLine($"Project Loaded: {project != null}");
                if (project != null)
                {
                    Console.WriteLine($"Project Name: {project.Name}");
                    Console.WriteLine($"Project Language: {project.Language}");
                    Console.WriteLine($"Documents Count: {project.Documents.Count()}");
                    Console.WriteLine($"AssemblyName: {project.AssemblyName}");
                    Console.WriteLine($"OutputFilePath: {project.OutputFilePath}");
                }

                // コンパイルを試みる
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                {
                    Console.WriteLine($"\n=== Compilation Info ===");
                    Console.WriteLine($"Assembly Name: {compilation.AssemblyName ?? "null"}");
                    Console.WriteLine($"Syntax Trees Count: {compilation.SyntaxTrees.Count()}");
                    Console.WriteLine($"References Count: {compilation.References.Count()}");
                    
                    var compilationDiagnostics = compilation.GetDiagnostics();
                    Console.WriteLine($"Compilation Diagnostics Count: {compilationDiagnostics.Length}");
                    foreach (var diag in compilationDiagnostics.Take(5))
                    {
                        Console.WriteLine($"  - {diag.Severity}: {diag.GetMessage()}");
                    }
                }

                // この時点で成功すれば、問題は他の場所にある
                Assert.IsNotNull(project, "Project should be loaded successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n=== Exception Details ===");
                Console.WriteLine($"Type: {ex.GetType().FullName}");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"\n=== Inner Exception ===");
                    Console.WriteLine($"Type: {ex.InnerException.GetType().FullName}");
                    Console.WriteLine($"Message: {ex.InnerException.Message}");
                    Console.WriteLine($"Stack Trace: {ex.InnerException.StackTrace}");
                }

                // "No file format header found" エラーの詳細を記録
                if (ex.Message.Contains("No file format header found"))
                {
                    Console.WriteLine("\n=== File Format Header Error Detected ===");
                    
                    // ファイルの最初の数バイトを確認
                    using (var fs = new FileStream(projectPath, FileMode.Open, FileAccess.Read))
                    {
                        var buffer = new byte[100];
                        var bytesRead = fs.Read(buffer, 0, buffer.Length);
                        Console.WriteLine($"First {bytesRead} bytes of project file:");
                        Console.WriteLine($"Hex: {BitConverter.ToString(buffer, 0, bytesRead)}");
                        Console.WriteLine($"Text: {System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead)}");
                    }

                    // BOMの確認
                    var bytes = await File.ReadAllBytesAsync(projectPath);
                    var hasBOM = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                    Console.WriteLine($"Has UTF-8 BOM: {hasBOM}");
                }

                throw; // エラーを再スロー
            }
            finally
            {
                Console.WriteLine($"\n=== Diagnostics Summary ===");
                Console.WriteLine($"Total Diagnostics: {diagnostics.Count}");
                foreach (var diag in diagnostics)
                {
                    Console.WriteLine($"  - {diag}");
                }
            }
        }

        [TestMethod]
        public async Task DebugStatelessWorkspaceFactory_WithDifferentProjectFormats()
        {
            // 異なるプロジェクトファイル形式でテスト
            var testCases = new[]
            {
                new { Name = "MinimalProject", Content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>" },
                new { Name = "WithBOM", Content = "\uFEFF" + @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>" },
                new { Name = "OldStyleProject", Content = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"" Condition=""Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')"" />
  <PropertyGroup>
    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>
    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>
    <ProjectGuid>{00000000-0000-0000-0000-000000000000}</ProjectGuid>
    <OutputType>Library</OutputType>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <RootNamespace>TestProject</RootNamespace>
    <AssemblyName>TestProject</AssemblyName>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
  </PropertyGroup>
  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />
</Project>" }
            };

            var discoveryService = new ProjectDiscoveryService();
            var workspaceFactory = new StatelessWorkspaceFactory(
                NullLogger<StatelessWorkspaceFactory>.Instance, 
                discoveryService);

            foreach (var testCase in testCases)
            {
                Console.WriteLine($"\n=== Testing {testCase.Name} ===");
                
                var projectPath = Path.Combine(_tempDirectory, $"{testCase.Name}.csproj");
                await File.WriteAllTextAsync(projectPath, testCase.Content);

                try
                {
                    var (workspace, project) = await workspaceFactory.CreateForProjectAsync(projectPath);
                    Console.WriteLine($"{testCase.Name}: Success - Project loaded");
                    workspace.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{testCase.Name}: Failed - {ex.Message}");
                    
                    if (ex.Message.Contains("No file format header found"))
                    {
                        // ファイル形式の詳細を出力
                        var bytes = await File.ReadAllBytesAsync(projectPath);
                        Console.WriteLine($"  File size: {bytes.Length} bytes");
                        Console.WriteLine($"  First 50 chars: {testCase.Content.Substring(0, Math.Min(50, testCase.Content.Length))}");
                        
                        // エラーが発生した具体的な場所を特定
                        Assert.Fail($"'{testCase.Name}' format should be supported but got 'No file format header found' error");
                    }
                }
            }
        }

        [TestMethod]
        public async Task DebugCreateForContextAsync_TraceExecutionPath()
        {
            // CreateForContextAsyncの実行パスを追跡
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            var codePath = Path.Combine(_tempDirectory, "Program.cs");

            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>");

            await File.WriteAllTextAsync(codePath, @"Console.WriteLine(""Hello World"");");

            var discoveryService = new ProjectDiscoveryService();
            var workspaceFactory = new StatelessWorkspaceFactory(
                NullLogger<StatelessWorkspaceFactory>.Instance, 
                discoveryService);

            Console.WriteLine("=== CreateForContextAsync Execution Trace ===");
            
            try
            {
                // プロジェクトファイルで直接テスト
                Console.WriteLine($"Testing with project file: {projectPath}");
                var (workspace1, context1, contextType1) = await workspaceFactory.CreateForContextAsync(projectPath);
                Console.WriteLine($"Success: ContextType={contextType1}");
                workspace1.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed with project file: {ex.Message}");
                if (ex.Message.Contains("No file format header found"))
                {
                    // エラーの詳細を記録
                    await LogFileFormatError(projectPath, ex);
                }
            }

            try
            {
                // C#ファイルでテスト（プロジェクト自動検出）
                Console.WriteLine($"\nTesting with C# file: {codePath}");
                var (workspace2, context2, contextType2) = await workspaceFactory.CreateForContextAsync(codePath);
                Console.WriteLine($"Success: ContextType={contextType2}");
                workspace2.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed with C# file: {ex.Message}");
            }
        }

        private async Task LogFileFormatError(string filePath, Exception ex)
        {
            Console.WriteLine("\n=== File Format Error Analysis ===");
            Console.WriteLine($"File: {filePath}");
            Console.WriteLine($"Exists: {File.Exists(filePath)}");
            
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                Console.WriteLine($"Size: {fileInfo.Length} bytes");
                Console.WriteLine($"Creation Time: {fileInfo.CreationTime}");
                Console.WriteLine($"Last Write Time: {fileInfo.LastWriteTime}");
                
                // ファイルの内容とエンコーディングを確認
                var content = await File.ReadAllTextAsync(filePath);
                Console.WriteLine($"Content Length: {content.Length} chars");
                Console.WriteLine($"First Line: {content.Split('\n').FirstOrDefault()}");
                
                // ファイルのバイト情報
                var bytes = await File.ReadAllBytesAsync(filePath);
                Console.WriteLine($"Byte Order Mark (BOM): {(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? "UTF-8 BOM" : "No BOM")}");
                
                // XML宣言の確認
                var hasXmlDeclaration = content.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"Has XML Declaration: {hasXmlDeclaration}");
                
                // Project要素の確認
                var hasProjectElement = content.Contains("<Project", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"Has <Project> Element: {hasProjectElement}");
            }
        }
    }
}