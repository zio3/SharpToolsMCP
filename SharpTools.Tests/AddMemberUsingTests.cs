using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Services;
using SharpTools.Tools.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using System.Text.Json;
using SharpTools.Tools.Mcp.Models;

namespace SharpTools.Tests
{
    [TestClass]
    public class AddMemberUsingTests
    {
        private string _testDataDirectory;
        private string _tempDirectory;
        private StatelessWorkspaceFactory _workspaceFactory;
        private ICodeModificationService _codeModificationService;
        private ICodeAnalysisService _codeAnalysisService;
        private IComplexityAnalysisService _complexityAnalysisService;
        private ISemanticSimilarityService _semanticSimilarityService;

        [TestInitialize]
        public void Setup()
        {
            // テスト用の一時ディレクトリを作成
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDirectory);

            // テストデータディレクトリのパスを取得
            _testDataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

            // StatelessWorkspaceFactoryを作成
            var discoveryService = new ProjectDiscoveryService();
            _workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, discoveryService);

            // サービスを作成
            var mockSolutionManager = new Mock<ISolutionManager>();
            _codeModificationService = new CodeModificationService(mockSolutionManager.Object, NullLogger<CodeModificationService>.Instance);
            _codeAnalysisService = new CodeAnalysisService(mockSolutionManager.Object, NullLogger<CodeAnalysisService>.Instance);
            
            var mockComplexityService = new Mock<IComplexityAnalysisService>();
            _complexityAnalysisService = mockComplexityService.Object;
            
            var mockSemanticService = new Mock<ISemanticSimilarityService>();
            _semanticSimilarityService = mockSemanticService.Object;
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
        public async Task AddMember_WithRequiredUsings_ShouldAddUsings()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
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

            // Act
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _codeModificationService,
                _codeAnalysisService,
                _complexityAnalysisService,
                _semanticSimilarityService,
                NullLogger<ModificationToolsLogCategory>.Instance,
                codePath,
                @"public int CalculateSum(IEnumerable<int> numbers) {
                    return numbers.Sum();
                }",
                "TestClass",
                "end",
                "auto",
                new[] { "System.Linq", "System.Collections.Generic" },
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            
            // JSON結果を解析
            var jsonResult = result.ToString();
            Assert.IsNotNull(jsonResult);
            
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 成功を確認
            Assert.IsTrue(root.GetProperty("success").GetBoolean());
            
            // 追加されたusing文を確認
            var addedUsings = root.GetProperty("addedUsings");
            Assert.AreEqual(2, addedUsings.GetArrayLength());
            
            var addedUsingsList = addedUsings.EnumerateArray().Select(u => u.GetString()).ToList();
            Assert.IsTrue(addedUsingsList.Contains("System.Linq"));
            Assert.IsTrue(addedUsingsList.Contains("System.Collections.Generic"));
            
            // コンフリクトがないことを確認
            var usingConflicts = root.GetProperty("usingConflicts");
            Assert.AreEqual(0, usingConflicts.GetArrayLength());
            
            // ファイルの内容を確認
            var updatedContent = await File.ReadAllTextAsync(codePath);
            Assert.IsTrue(updatedContent.Contains("using System.Collections.Generic;"));
            Assert.IsTrue(updatedContent.Contains("using System.Linq;"));
        }

        [TestMethod]
        public async Task AddMember_WithExistingUsings_ShouldReportConflicts()
        {
            // Arrange
            var testCode = @"using System.Linq;

namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
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

            // Act
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _codeModificationService,
                _codeAnalysisService,
                _complexityAnalysisService,
                _semanticSimilarityService,
                NullLogger<ModificationToolsLogCategory>.Instance,
                codePath,
                @"public int Count() => items.Count();",
                "TestClass",
                "end",
                "auto",
                new[] { "System.Linq", "System.Collections.Generic" },
                CancellationToken.None
            );

            // Assert
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 追加されたusing文を確認（System.Collections.Genericのみ）
            var addedUsings = root.GetProperty("addedUsings");
            Assert.AreEqual(1, addedUsings.GetArrayLength());
            Assert.AreEqual("System.Collections.Generic", addedUsings.EnumerateArray().First().GetString());
            
            // コンフリクトを確認（System.Linqは既に存在）
            var usingConflicts = root.GetProperty("usingConflicts");
            Assert.AreEqual(1, usingConflicts.GetArrayLength());
            Assert.AreEqual("System.Linq", usingConflicts.EnumerateArray().First().GetString());
        }

        [TestMethod]
        public async Task AddMember_WithManualStrategy_ShouldNotAddUsings()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
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

            // Act
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _codeModificationService,
                _codeAnalysisService,
                _complexityAnalysisService,
                _semanticSimilarityService,
                NullLogger<ModificationToolsLogCategory>.Instance,
                codePath,
                @"public void PrintMessage() => Console.WriteLine(""Hello"");",
                "TestClass",
                "end",
                "auto",
                null, // appendUsingsを指定しない
                CancellationToken.None
            );

            // Assert
            var jsonResult = result.ToString();
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // using文が追加されていないことを確認
            var addedUsings = root.GetProperty("addedUsings");
            Assert.AreEqual(0, addedUsings.GetArrayLength());
            
            // ファイルの内容を確認
            var updatedContent = await File.ReadAllTextAsync(codePath);
            Assert.IsFalse(updatedContent.Contains("using System;"));
        }

        [TestMethod]
        public async Task AddMember_WithAlphabeticalOrder_ShouldInsertCorrectly()
        {
            // Arrange
            var testCode = @"using System;
using System.Text;

namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
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

            // Act
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _codeModificationService,
                _codeAnalysisService,
                _complexityAnalysisService,
                _semanticSimilarityService,
                NullLogger<ModificationToolsLogCategory>.Instance,
                codePath,
                @"public List<string> GetItems() => new List<string>();",
                "TestClass",
                "end",
                "auto",
                new[] { "System.Linq", "System.Collections.Generic" },
                CancellationToken.None
            );

            // Assert
            var updatedContent = await File.ReadAllTextAsync(codePath);
            
            // using文の順序を確認
            var lines = updatedContent.Split('\n');
            var usingLines = lines.Where(l => l.Trim().StartsWith("using ")).ToList();
            
            Assert.AreEqual(4, usingLines.Count);
            Assert.IsTrue(usingLines[0].Contains("System;"));
            Assert.IsTrue(usingLines[1].Contains("System.Collections.Generic;"));
            Assert.IsTrue(usingLines[2].Contains("System.Linq;"));
            Assert.IsTrue(usingLines[3].Contains("System.Text;"));
        }
    }
}