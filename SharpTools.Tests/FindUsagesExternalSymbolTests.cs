using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Services;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Mcp.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using Moq;

namespace SharpTools.Tests
{
    [TestClass]
    public class FindUsagesExternalSymbolTests
    {
        private string _tempDirectory;
        private StatelessWorkspaceFactory _workspaceFactory;
        
        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDirectory);
            
            var discoveryService = new ProjectDiscoveryService();
            _workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, discoveryService);
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
        
        /// <summary>
        /// 外部ライブラリのシンボル（ILogger）を検索できることを確認
        /// </summary>
        [TestMethod]
        public async Task FindUsages_ExternalSymbol_ILogger_ShouldFind()
        {
            // Arrange
            var projectName = "TestProject";
            var projectPath = Path.Combine(_tempDirectory, $"{projectName}.csproj");
            var codePath = Path.Combine(_tempDirectory, "HomeController.cs");
            
            // プロジェクトファイルを作成（Microsoft.Extensions.Logging参照付き）
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""9.0.0"" />
  </ItemGroup>
</Project>");
            
            // HomeControllerのコードを作成
            await File.WriteAllTextAsync(codePath, @"using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class HomeController
    {
        private readonly ILogger<HomeController> _logger;
        
        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }
        
        public void Index()
        {
            _logger.LogInformation(""Home page accessed"");
        }
    }
}");
            
            // Act - ILoggerを検索（外部シンボル検索有効）
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
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
            
            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // シンボルが見つかることを確認
            Assert.IsTrue(root.GetProperty("symbolsFound").GetArrayLength() > 0, "ILogger symbol should be found");
            Assert.IsTrue(root.GetProperty("totalReferences").GetInt32() > 0, "ILogger references should be found");
            
            // メッセージを確認（見つからないメッセージが出ていないこと）
            if (root.TryGetProperty("message", out var message))
            {
                Assert.IsFalse(message.GetString()?.Contains("見つかりませんでした") ?? false, 
                    "Should not have 'not found' message");
            }
        }
        
        /// <summary>
        /// プライベートフィールド（_logger）を検索できることを確認
        /// </summary>
        [TestMethod]
        public async Task FindUsages_PrivateField_Logger_ShouldFind()
        {
            // Arrange
            var projectName = "TestProject";
            var projectPath = Path.Combine(_tempDirectory, $"{projectName}.csproj");
            var codePath = Path.Combine(_tempDirectory, "HomeController.cs");
            
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""9.0.0"" />
  </ItemGroup>
</Project>");
            
            await File.WriteAllTextAsync(codePath, @"using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class HomeController
    {
        private readonly ILogger<HomeController> _logger;
        
        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }
        
        public void Index()
        {
            _logger.LogInformation(""Home page accessed"");
        }
    }
}");
            
            // Act - _loggerを検索
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
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
            
            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // _loggerフィールドが見つかることを確認
            Assert.IsTrue(root.GetProperty("symbolsFound").GetArrayLength() > 0, "_logger field should be found");
            
            // 使用箇所が見つかることを確認（コンストラクタでの代入とLogInformationでの使用）
            Assert.IsTrue(root.GetProperty("totalReferences").GetInt32() >= 2, 
                "_logger should have at least 2 references");
        }
        
        /// <summary>
        /// 拡張メソッド（LogInformation）を検索できることを確認
        /// </summary>
        [TestMethod]
        public async Task FindUsages_ExtensionMethod_LogInformation_ShouldFind()
        {
            // Arrange
            var projectName = "TestProject";
            var projectPath = Path.Combine(_tempDirectory, $"{projectName}.csproj");
            var codePath = Path.Combine(_tempDirectory, "HomeController.cs");
            
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""9.0.0"" />
  </ItemGroup>
</Project>");
            
            await File.WriteAllTextAsync(codePath, @"using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class HomeController
    {
        private readonly ILogger<HomeController> _logger;
        
        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }
        
        public void Index()
        {
            _logger.LogInformation(""Home page accessed"");
            _logger.LogInformation(""Another log message"");
        }
    }
}");
            
            // Act - LogInformationを検索
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "LogInformation",
                maxResults: 100,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: true,
                searchMode: "all",
                CancellationToken.None
            );
            
            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // LogInformation拡張メソッドが見つかることを確認
            Assert.IsTrue(root.GetProperty("symbolsFound").GetArrayLength() > 0, "LogInformation should be found");
            
            // 2回の使用箇所が見つかることを確認
            Assert.IsTrue(root.GetProperty("totalReferences").GetInt32() >= 2, 
                "LogInformation should have at least 2 references");
        }
        
        /// <summary>
        /// searchMode="declaration"では外部シンボルが見つからないことを確認
        /// </summary>
        [TestMethod]
        public async Task FindUsages_DeclarationMode_ExternalSymbol_ShouldNotFind()
        {
            // Arrange
            var projectName = "TestProject";
            var projectPath = Path.Combine(_tempDirectory, $"{projectName}.csproj");
            var codePath = Path.Combine(_tempDirectory, "HomeController.cs");
            
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""9.0.0"" />
  </ItemGroup>
</Project>");
            
            await File.WriteAllTextAsync(codePath, @"using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class HomeController
    {
        private readonly ILogger<HomeController> _logger;
        
        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }
    }
}");
            
            // Act - ILoggerを検索（declarationモード）
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "ILogger",
                maxResults: 100,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: true,
                searchMode: "declaration", // 宣言のみモード
                CancellationToken.None
            );
            
            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 外部シンボルは宣言されていないので見つからない
            Assert.AreEqual(0, root.GetProperty("symbolsFound").GetArrayLength(), 
                "ILogger should not be found in declaration mode");
            
            // "見つかりませんでした"メッセージが出ることを確認
            if (root.TryGetProperty("message", out var message))
            {
                Assert.IsTrue(message.GetString()?.Contains("見つかりませんでした") ?? false, 
                    "Should have 'not found' message");
            }
        }
        
        /// <summary>
        /// includeExternalSymbols=falseでは外部シンボルが見つからないことを確認
        /// </summary>
        [TestMethod]
        public async Task FindUsages_ExternalSymbolsDisabled_ShouldNotFind()
        {
            // Arrange
            var projectName = "TestProject";
            var projectPath = Path.Combine(_tempDirectory, $"{projectName}.csproj");
            var codePath = Path.Combine(_tempDirectory, "HomeController.cs");
            
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""9.0.0"" />
  </ItemGroup>
</Project>");
            
            await File.WriteAllTextAsync(codePath, @"using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class HomeController
    {
        private readonly ILogger<HomeController> _logger;
    }
}");
            
            // Act - ILoggerを検索（外部シンボル無効）
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "ILogger",
                maxResults: 100,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: false, // 外部シンボル検索無効
                searchMode: "all",
                CancellationToken.None
            );
            
            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 外部シンボルは見つからない
            Assert.AreEqual(0, root.GetProperty("symbolsFound").GetArrayLength(), 
                "ILogger should not be found when includeExternalSymbols is false");
        }
    }
}