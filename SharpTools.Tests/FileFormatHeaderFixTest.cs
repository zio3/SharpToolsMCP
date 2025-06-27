using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpTools.Tools.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpTools.Tests
{
    /// <summary>
    /// "No file format header found" エラーの修正案を検証するテスト
    /// </summary>
    [TestClass]
    public class FileFormatHeaderFixTest
    {
        private string _tempDirectory;

        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsFix_{Guid.NewGuid()}");
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
        public async Task TestMSBuildWorkspaceWithDifferentConfigurations()
        {
            // 異なるMSBuildWorkspace設定でテスト
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            await CreateMinimalProject(projectPath);

            var configurations = new[]
            {
                new Dictionary<string, string>(), // デフォルト設定
                new Dictionary<string, string>
                {
                    ["DesignTimeBuild"] = "true",
                    ["BuildingInsideVisualStudio"] = "true"
                },
                new Dictionary<string, string>
                {
                    ["Configuration"] = "Debug",
                    ["Platform"] = "AnyCPU"
                },
                new Dictionary<string, string>
                {
                    ["SkipCompilerExecution"] = "true",
                    ["ProvideCommandLineArgs"] = "true"
                },
                new Dictionary<string, string>
                {
                    ["MSBuildEnableWorkloadResolver"] = "false"
                }
            };

            foreach (var config in configurations)
            {
                Console.WriteLine($"\n=== Testing with configuration: {string.Join(", ", config.Select(kvp => $"{kvp.Key}={kvp.Value}"))} ===");
                
                try
                {
                    using var workspace = MSBuildWorkspace.Create(config);
                    var project = await workspace.OpenProjectAsync(projectPath);
                    Console.WriteLine("Success: Project loaded");
                    Assert.IsNotNull(project);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed: {ex.Message}");
                    if (ex.Message.Contains("No file format header found"))
                    {
                        // この設定でエラーが発生することを記録
                        Console.WriteLine("ERROR: 'No file format header found' detected with this configuration");
                    }
                }
            }
        }

        [TestMethod]
        public async Task TestProjectFileEncodingVariations()
        {
            // 異なるエンコーディングでプロジェクトファイルを作成
            var encodingTests = new (string Name, Encoding Encoding)[]
            {
                ("UTF8_NoBOM", new UTF8Encoding(false)),
                ("UTF8_WithBOM", new UTF8Encoding(true)),
                ("UTF16LE", Encoding.Unicode),
                ("UTF16BE", Encoding.BigEndianUnicode),
                ("ASCII", Encoding.ASCII)
            };

            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>";

            foreach (var test in encodingTests)
            {
                Console.WriteLine($"\n=== Testing {test.Name} encoding ===");
                var projectPath = Path.Combine(_tempDirectory, $"{test.Name}.csproj");
                
                // 指定されたエンコーディングでファイルを作成
                await File.WriteAllTextAsync(projectPath, projectContent, test.Encoding);
                
                // ファイルの実際のバイトを確認
                var bytes = await File.ReadAllBytesAsync(projectPath);
                Console.WriteLine($"File size: {bytes.Length} bytes");
                Console.WriteLine($"First 10 bytes: {BitConverter.ToString(bytes.Take(10).ToArray())}");
                
                try
                {
                    using var workspace = MSBuildWorkspace.Create();
                    var project = await workspace.OpenProjectAsync(projectPath);
                    Console.WriteLine($"{test.Name}: Success");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{test.Name}: Failed - {ex.Message}");
                    if (ex.Message.Contains("No file format header found"))
                    {
                        Assert.Fail($"Encoding '{test.Name}' should be supported but got 'No file format header found' error");
                    }
                }
            }
        }

        [TestMethod]
        public async Task TestMSBuildLocatorInitialization()
        {
            // MSBuildLocatorの初期化状態をテスト
            Console.WriteLine("=== MSBuildLocator Initialization Test ===");
            Console.WriteLine($"Already Registered: {MSBuildLocator.IsRegistered}");
            
            if (!MSBuildLocator.IsRegistered)
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
                Console.WriteLine($"Available MSBuild instances: {instances.Length}");
                
                foreach (var instance in instances)
                {
                    Console.WriteLine($"\nInstance: {instance.Name}");
                    Console.WriteLine($"  Version: {instance.Version}");
                    Console.WriteLine($"  MSBuildPath: {instance.MSBuildPath}");
                    Console.WriteLine($"  VisualStudioRootPath: {instance.VisualStudioRootPath}");
                }

                // 異なるインスタンスでテスト
                foreach (var instance in instances.Take(2)) // 最初の2つだけテスト
                {
                    Console.WriteLine($"\n=== Testing with MSBuild instance: {instance.Name} ===");
                    
                    // 新しいプロセスでテストする必要があるため、ここではスキップ
                    Console.WriteLine("Note: MSBuildLocator can only be registered once per process");
                }
            }

            // 現在の登録でテスト
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            await CreateMinimalProject(projectPath);
            
            try
            {
                using var workspace = MSBuildWorkspace.Create();
                var project = await workspace.OpenProjectAsync(projectPath);
                Console.WriteLine("Current MSBuildLocator registration works correctly");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error with current registration: {ex.Message}");
            }
        }

        [TestMethod]
        public async Task TestProjectFilePathFormats()
        {
            // 異なるパス形式でテスト
            await CreateMinimalProject(Path.Combine(_tempDirectory, "TestProject.csproj"));
            
            var pathTests = new (string Name, Func<string> GetPath)[]
            {
                ("AbsolutePath", () => Path.Combine(_tempDirectory, "TestProject.csproj")),
                ("RelativePath", () => Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(_tempDirectory, "TestProject.csproj"))),
                ("WithForwardSlashes", () => Path.Combine(_tempDirectory, "TestProject.csproj").Replace('\\', '/')),
                ("WithBackslashes", () => Path.Combine(_tempDirectory, "TestProject.csproj").Replace('/', '\\')),
                ("WithTrailingSlash", () => _tempDirectory + Path.DirectorySeparatorChar + "TestProject.csproj")
            };

            foreach (var test in pathTests)
            {
                Console.WriteLine($"\n=== Testing {test.Name} ===");
                var path = test.GetPath();
                Console.WriteLine($"Path: {path}");
                Console.WriteLine($"Exists: {File.Exists(path)}");
                
                try
                {
                    using var workspace = MSBuildWorkspace.Create();
                    var project = await workspace.OpenProjectAsync(path);
                    Console.WriteLine($"{test.Name}: Success");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{test.Name}: Failed - {ex.Message}");
                    if (ex.Message.Contains("No file format header found"))
                    {
                        // パス形式が原因の可能性
                        Console.WriteLine($"ERROR: Path format '{test.Name}' caused 'No file format header found' error");
                    }
                }
            }
        }

        [TestMethod]
        public async Task TestWorkspaceFactoryErrorHandling()
        {
            // StatelessWorkspaceFactoryのエラーハンドリングをテスト
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            await CreateMinimalProject(projectPath);
            
            var discoveryService = new ProjectDiscoveryService();
            var logger = new TestLogger<StatelessWorkspaceFactory>();
            var workspaceFactory = new StatelessWorkspaceFactory(logger, discoveryService);

            try
            {
                var (workspace, project) = await workspaceFactory.CreateForProjectAsync(projectPath);
                Console.WriteLine("Project loaded successfully");
                workspace.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                
                // ログメッセージを確認
                Console.WriteLine("\n=== Logged Messages ===");
                foreach (var log in logger.LoggedMessages)
                {
                    Console.WriteLine($"[{log.LogLevel}] {log.Message}");
                }

                if (ex.Message.Contains("No file format header found"))
                {
                    // エラーの詳細情報を収集
                    Console.WriteLine("\n=== Error Context ===");
                    Console.WriteLine($"Project Path: {projectPath}");
                    Console.WriteLine($"Working Directory: {Directory.GetCurrentDirectory()}");
                    Console.WriteLine($"Temp Directory: {Path.GetTempPath()}");
                    
                    // 環境変数を確認
                    Console.WriteLine("\n=== Relevant Environment Variables ===");
                    var relevantVars = new[] { "MSBuildSDKsPath", "MSBuildExtensionsPath", "DOTNET_ROOT", "PATH" };
                    foreach (var varName in relevantVars)
                    {
                        var value = Environment.GetEnvironmentVariable(varName);
                        if (!string.IsNullOrEmpty(value))
                        {
                            Console.WriteLine($"{varName}: {value}");
                        }
                    }
                }
            }
        }

        private async Task CreateMinimalProject(string projectPath)
        {
            var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>";
            
            await File.WriteAllTextAsync(projectPath, content);
            
            // 同じディレクトリに簡単なC#ファイルも作成
            var codePath = Path.Combine(Path.GetDirectoryName(projectPath), "Program.cs");
            await File.WriteAllTextAsync(codePath, @"Console.WriteLine(""Hello World"");");
        }

        // テスト用のロガー実装
        private class TestLogger<T> : ILogger<T>
        {
            public List<(LogLevel LogLevel, string Message)> LoggedMessages { get; } = new List<(LogLevel, string)>();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopDisposable();
            public bool IsEnabled(LogLevel logLevel) => true;
            
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                LoggedMessages.Add((logLevel, message));
            }

            private class NoopDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}