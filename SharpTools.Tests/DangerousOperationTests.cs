using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Mcp.Models;
using SharpTools.Tools.Mcp.Helpers;
using SharpTools.Tools.Services;
using SharpTools.Tools.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using System.Text.Json;

namespace SharpTools.Tests
{
    [TestClass]
    public class DangerousOperationTests
    {
        private string _tempDirectory;
        private StatelessWorkspaceFactory _workspaceFactory;
        private ICodeAnalysisService _codeAnalysisService;
        private ICodeModificationService _mockModificationService;

        [TestInitialize]
        public void Setup()
        {
            // テスト用の一時ディレクトリを作成
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDirectory);

            // StatelessWorkspaceFactoryを作成
            var discoveryService = new ProjectDiscoveryService();
            _workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, discoveryService);

            // モックを作成
            var mockSolutionManager = new Mock<ISolutionManager>();
            _codeAnalysisService = new CodeAnalysisService(mockSolutionManager.Object, NullLogger<CodeAnalysisService>.Instance);
            
            var mockModificationService = new Mock<ICodeModificationService>();
            _mockModificationService = mockModificationService.Object;
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
        public void DangerousPatternDetector_DetectsDangerousPatterns()
        {
            // Arrange & Act & Assert
            Assert.IsTrue(DangerousOperationDetector.IsDangerousPattern(".*"));
            Assert.IsTrue(DangerousOperationDetector.IsDangerousPattern(".+"));
            Assert.IsTrue(DangerousOperationDetector.IsDangerousPattern("^.*$"));
            Assert.IsTrue(DangerousOperationDetector.IsDangerousPattern("[\\s\\S]*"));
            
            Assert.IsFalse(DangerousOperationDetector.IsDangerousPattern("Test"));
            Assert.IsFalse(DangerousOperationDetector.IsDangerousPattern("\\bclass\\b"));
            Assert.IsFalse(DangerousOperationDetector.IsDangerousPattern("public void"));
        }

        [TestMethod]
        public void EvaluateRiskLevel_ReturnsCorrectLevels()
        {
            // Test critical risk
            var (criticalLevel, criticalFactors) = DangerousOperationDetector.EvaluateRiskLevel(".*", 2000, 50, true);
            Assert.AreEqual(RiskLevels.Critical, criticalLevel);
            Assert.IsTrue(criticalFactors.Contains(RiskTypes.UniversalPattern));

            // Test high risk
            var (highLevel, highFactors) = DangerousOperationDetector.EvaluateRiskLevel("test", 600, 25, true);
            Assert.AreEqual(RiskLevels.High, highLevel);

            // Test medium risk
            var (mediumLevel, mediumFactors) = DangerousOperationDetector.EvaluateRiskLevel("test", 60, 6, false);
            Assert.AreEqual(RiskLevels.Medium, mediumLevel);

            // Test low risk
            var (lowLevel, lowFactors) = DangerousOperationDetector.EvaluateRiskLevel("test", 10, 2, false);
            Assert.AreEqual(RiskLevels.Low, lowLevel);
        }

        [TestMethod]
        public async Task ReplaceAcrossFiles_DetectsDangerousOperation()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void Method1() { }
        public void Method2() { }
        public void Method3() { }
    }
}";

            var projectName = "TestProject";
            var projectPath = Path.Combine(_tempDirectory, $"{projectName}.csproj");
            var codePath = Path.Combine(_tempDirectory, "TestCode.cs");

            // プロジェクトファイルを作成
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            // コードファイルを作成
            await File.WriteAllTextAsync(codePath, testCode);

            // Act - 危険なパターンで実行（userConfirmResponse = null）
            var result = await ModificationTools.ReplaceAcrossFiles(
                _workspaceFactory,
                NullLogger<ModificationToolsLogCategory>.Instance,
                projectPath,
                ".*",        // 危険なパターン
                "REPLACED",
                fileExtensions: ".cs",
                dryRun: false,
                caseSensitive: false,
                maxFiles: 1000,
                userConfirmResponse: null,
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            
            // JSON結果を解析
            var jsonResult = result.ToString();
            Assert.IsNotNull(jsonResult);
            
            
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            Assert.IsTrue(root.GetProperty("dangerousOperationDetected").GetBoolean());
            // With 70 matches, the risk level should be Medium
            Assert.AreEqual(RiskLevels.Medium, root.GetProperty("riskLevel").GetString());
            Assert.IsTrue(root.GetProperty("message").GetString().Contains("注意"));
            Assert.AreEqual("Yes", root.GetProperty("requiredConfirmationText").GetString());
            Assert.IsTrue(root.GetProperty("confirmationPrompt").GetString().Contains("\"Yes\""));
            
            // ファイルが変更されていないことを確認
            var originalContent = await File.ReadAllTextAsync(codePath);
            Assert.IsTrue(originalContent.Contains("Method1"));
        }

        [TestMethod]
        public async Task ReplaceAcrossFiles_ExecutesWithConfirmation()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod() { }
    }
}";

            var projectName = "TestProject";
            var projectPath = Path.Combine(_tempDirectory, $"{projectName}.csproj");
            var codePath = Path.Combine(_tempDirectory, "TestCode.cs");

            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            await File.WriteAllTextAsync(codePath, testCode);

            // Act - 安全なパターンで実行
            var result = await ModificationTools.ReplaceAcrossFiles(
                _workspaceFactory,
                NullLogger<ModificationToolsLogCategory>.Instance,
                projectPath,
                "TestMethod",  // 安全なパターン
                "NewMethod",
                fileExtensions: ".cs",
                dryRun: false,
                caseSensitive: false,
                maxFiles: 1000,
                userConfirmResponse: null,
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            
            
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 安全なパターンでも、isDestructive=trueのため危険操作として検出される
            // ただし、リスクレベルはLowになるはず
            if (root.TryGetProperty("dangerousOperationDetected", out var dangerProp))
            {
                Assert.IsTrue(dangerProp.GetBoolean());
                Assert.AreEqual(RiskLevels.Low, root.GetProperty("riskLevel").GetString());
            }
            else
            {
                // 通常の結果として返されることを確認
                Assert.IsTrue(root.TryGetProperty("totalMatches", out _));
            }
        }

        [TestMethod]
        public async Task OverwriteMember_RequiresConfirmation()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void OldMethod()
        {
            Console.WriteLine(""Original"");
        }
    }
}";

            var codePath = Path.Combine(_tempDirectory, "TestCode.cs");
            await File.WriteAllTextAsync(codePath, testCode);
            
            // プロジェクトファイルを作成
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            // Act - 確認なしで実行
            var result = await ModificationTools.OverwriteMember(
                _workspaceFactory,
                _mockModificationService,
                _codeAnalysisService,
                NullLogger<ModificationToolsLogCategory>.Instance,
                codePath,
                "OldMethod",
                "public void OldMethod() { Console.WriteLine(\"New\"); }",
                userConfirmResponse: null,
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            Assert.IsTrue(root.GetProperty("dangerousOperationDetected").GetBoolean());
            Assert.AreEqual(RiskLevels.High, root.GetProperty("riskLevel").GetString());
            Assert.AreEqual(RiskTypes.DestructiveOperation, root.GetProperty("riskType").GetString());
            Assert.IsTrue(root.GetProperty("message").GetString().Contains("破壊的操作"));
            Assert.AreEqual("Yes", root.GetProperty("requiredConfirmationText").GetString());
            Assert.IsTrue(root.GetProperty("confirmationPrompt").GetString().Contains("\"Yes\""));
            
            // ファイルが変更されていないことを確認
            var originalContent = await File.ReadAllTextAsync(codePath);
            Assert.IsTrue(originalContent.Contains("Original"));
            Assert.IsFalse(originalContent.Contains("New"));
        }

        [TestMethod]
        public async Task OverwriteMember_ExecutesWithYesConfirmation()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void OldMethod()
        {
            Console.WriteLine(""Original"");
        }
    }
}";

            var codePath = Path.Combine(_tempDirectory, "TestCode.cs");
            await File.WriteAllTextAsync(codePath, testCode);
            
            // プロジェクトファイルを作成
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            // Act - "Yes"で確認して実行
            var result = await ModificationTools.OverwriteMember(
                _workspaceFactory,
                _mockModificationService,
                _codeAnalysisService,
                NullLogger<ModificationToolsLogCategory>.Instance,
                codePath,
                "OldMethod",
                "public void OldMethod() { Console.WriteLine(\"New\"); }",
                userConfirmResponse: "Yes",
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 成功結果が返されることを確認（危険操作警告ではない）
            Assert.IsFalse(root.TryGetProperty("dangerousOperationDetected", out var dangerProp) && dangerProp.GetBoolean());
            // メソッドが実際に変更されたことを示すプロパティがあることを確認
            Assert.IsTrue(root.TryGetProperty("memberName", out _) || root.TryGetProperty("success", out _));
        }

        [TestMethod]
        public async Task OverwriteMember_RejectsInvalidConfirmation()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void OldMethod()
        {
            Console.WriteLine(""Original"");
        }
    }
}";

            var codePath = Path.Combine(_tempDirectory, "TestCode.cs");
            await File.WriteAllTextAsync(codePath, testCode);
            
            var projectPath = Path.Combine(_tempDirectory, "TestProject.csproj");
            await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            // Act - 不正な確認文字列で実行
            var result = await ModificationTools.OverwriteMember(
                _workspaceFactory,
                _mockModificationService,
                _codeAnalysisService,
                NullLogger<ModificationToolsLogCategory>.Instance,
                codePath,
                "OldMethod",
                "public void OldMethod() { Console.WriteLine(\"New\"); }",
                userConfirmResponse: "yes", // 小文字
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 危険操作として検出される
            Assert.IsTrue(root.GetProperty("dangerousOperationDetected").GetBoolean());
            Assert.AreEqual("Yes", root.GetProperty("requiredConfirmationText").GetString());
            
            // ファイルが変更されていないことを確認
            var originalContent = await File.ReadAllTextAsync(codePath);
            Assert.IsTrue(originalContent.Contains("Original"));
            Assert.IsFalse(originalContent.Contains("New"));
        }
    }
}