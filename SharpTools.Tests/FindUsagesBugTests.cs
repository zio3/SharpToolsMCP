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

namespace SharpTools.Tests
{
    [TestClass]
    public class FindUsagesBugTests
    {
        private string _testDataDirectory;
        private string _tempDirectory;
        private StatelessWorkspaceFactory _workspaceFactory;
        private ICodeAnalysisService _codeAnalysisService;
        private IFuzzyFqnLookupService _fuzzyFqnLookupService;

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

            // モックを作成
            var mockSolutionManager = new Mock<ISolutionManager>();
            _codeAnalysisService = new CodeAnalysisService(mockSolutionManager.Object, NullLogger<CodeAnalysisService>.Instance);
            _fuzzyFqnLookupService = new FuzzyFqnLookupService(NullLogger<FuzzyFqnLookupService>.Instance);
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
        public async Task FindUsages_ClassSymbol_ShouldReturnUsages()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
        public int Value { get; set; }
        
        public void TestMethod()
        {
            Console.WriteLine(""Test"");
        }
    }
    
    public class UsageClass
    {
        public void UseTestClass()
        {
            var test = new TestClass();
            test.Name = ""Test"";
            test.Value = 42;
            test.TestMethod();
        }
        
        public TestClass GetTestClass()
        {
            return new TestClass { Name = ""Result"", Value = 100 };
        }
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
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "TestClass",
                maxResults: 100,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: false,
                searchMode: "declaration",
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            
            // JSON結果を解析
            var jsonResult = result.ToString();
            Assert.IsNotNull(jsonResult);
            
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 検索語の確認
            Assert.AreEqual("TestClass", root.GetProperty("searchTerm").GetString());
            
            // 見つかったシンボルの確認
            var symbolsFound = root.GetProperty("symbolsFound");
            Assert.IsTrue(symbolsFound.GetArrayLength() > 0, "Should find at least one symbol");
            
            var firstSymbol = symbolsFound.EnumerateArray().First();
            Assert.AreEqual("TestClass", firstSymbol.GetProperty("name").GetString());
            Assert.AreEqual("NamedType", firstSymbol.GetProperty("kind").GetString());
            
            // 参照の確認
            var references = root.GetProperty("references");
            Assert.IsTrue(references.GetArrayLength() > 0, "Should find at least one file with references");
            
            // 使用箇所の詳細確認
            var foundConstructorUsage = false;
            var foundTypeUsage = false;
            
            foreach (var fileRef in references.EnumerateArray())
            {
                var locations = fileRef.GetProperty("locations");
                foreach (var location in locations.EnumerateArray())
                {
                    var context = location.GetProperty("context").GetString();
                    if (context != null && context.Contains("new TestClass"))
                    {
                        foundConstructorUsage = true;
                    }
                    if (context != null && context.Contains("TestClass GetTestClass"))
                    {
                        foundTypeUsage = true;
                    }
                }
            }
            
            Assert.IsTrue(foundConstructorUsage, "Should find constructor usage");
            Assert.IsTrue(foundTypeUsage, "Should find type usage");
        }

        [TestMethod]
        public async Task FindUsages_MethodSymbol_ShouldReturnMethodCalls()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TargetMethod(string message)
        {
            Console.WriteLine(message);
        }
        
        public void CallerMethod1()
        {
            TargetMethod(""From CallerMethod1"");
        }
        
        public void CallerMethod2()
        {
            var msg = ""Dynamic message"";
            TargetMethod(msg);
            this.TargetMethod(""Explicit this"");
        }
    }
    
    public class ExternalClass
    {
        public void UseTargetMethod()
        {
            var test = new TestClass();
            test.TargetMethod(""From ExternalClass"");
        }
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
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "TargetMethod",
                maxResults: 50,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: false,
                searchMode: "declaration",
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            
            var jsonResult = result.ToString();
            Assert.IsNotNull(jsonResult);
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 検索語の確認
            Assert.AreEqual("TargetMethod", root.GetProperty("searchTerm").GetString());
            
            // 見つかったシンボルの確認
            var symbolsFound = root.GetProperty("symbolsFound");
            Assert.IsTrue(symbolsFound.GetArrayLength() > 0, "Should find at least one symbol");
            
            var firstSymbol = symbolsFound.EnumerateArray().First();
            Assert.AreEqual("TargetMethod", firstSymbol.GetProperty("name").GetString());
            Assert.AreEqual("Method", firstSymbol.GetProperty("kind").GetString());
            
            // 参照の確認
            var references = root.GetProperty("references");
            Assert.IsTrue(references.GetArrayLength() > 0, "Should find method calls");
            
            // 呼び出し箇所の確認
            var callCount = 0;
            foreach (var fileRef in references.EnumerateArray())
            {
                var locations = fileRef.GetProperty("locations");
                callCount += locations.GetArrayLength();
            }
            
            Assert.IsTrue(callCount >= 4, $"Should find at least 4 method calls, found {callCount}");
        }

        [TestMethod]
        public async Task FindUsages_PropertySymbol_ShouldReturnPropertyAccess()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string Name { get; set; }
        
        public void SetName(string value)
        {
            Name = value; // Property setter usage
        }
        
        public string GetName()
        {
            return Name; // Property getter usage
        }
        
        public void PrintName()
        {
            Console.WriteLine($""Name is: {Name}""); // Property getter usage
        }
    }
    
    public class UsageClass
    {
        public void UseProperty()
        {
            var test = new TestClass();
            test.Name = ""Test Value""; // External setter usage
            var name = test.Name; // External getter usage
            
            if (test.Name != null) // External getter usage
            {
                Console.WriteLine(test.Name.Length);
            }
        }
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
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "Name",
                maxResults: 50,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: false,
                searchMode: "declaration",
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            
            var jsonResult = result.ToString();
            Assert.IsNotNull(jsonResult);
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 検索語の確認
            Assert.AreEqual("Name", root.GetProperty("searchTerm").GetString());
            
            // 見つかったシンボルの確認
            var symbolsFound = root.GetProperty("symbolsFound");
            Assert.IsTrue(symbolsFound.GetArrayLength() > 0, "Should find at least one symbol");
            
            var firstSymbol = symbolsFound.EnumerateArray().First();
            Assert.AreEqual("Name", firstSymbol.GetProperty("name").GetString());
            Assert.AreEqual("Property", firstSymbol.GetProperty("kind").GetString());
            
            // 参照の確認
            var references = root.GetProperty("references");
            Assert.IsTrue(references.GetArrayLength() > 0, "Should find property usages");
            
            // プロパティアクセスの確認
            var accessCount = 0;
            foreach (var fileRef in references.EnumerateArray())
            {
                var locations = fileRef.GetProperty("locations");
                accessCount += locations.GetArrayLength();
            }
            
            Assert.IsTrue(accessCount >= 5, $"Should find at least 5 property accesses, found {accessCount}");
        }

        [TestMethod]
        public async Task FindUsages_NonExistentSymbol_ShouldReturnNotFound()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void ExistingMethod()
        {
            Console.WriteLine(""This method exists"");
        }
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
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "NonExistentMethod",
                maxResults: 50,
                includePrivateMembers: true,
                includeInheritedMembers: false,
                includeExternalSymbols: false,
                searchMode: "declaration",
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            
            var jsonResult = result.ToString();
            Assert.IsNotNull(jsonResult);
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 検索語の確認
            Assert.AreEqual("NonExistentMethod", root.GetProperty("searchTerm").GetString());
            
            // メッセージの確認
            var message = root.GetProperty("message").GetString();
            Assert.IsNotNull(message, "Should have a message for non-existent symbol");
            Assert.IsTrue(message.Contains("見つかりませんでした"), 
                $"Message should indicate symbol not found: {message}");
            
            // 見つかったシンボルは空であるべき
            var symbolsFound = root.GetProperty("symbolsFound");
            Assert.AreEqual(0, symbolsFound.GetArrayLength(), "Should not find any symbols");
            
            // 参照も空であるべき
            var references = root.GetProperty("references");
            Assert.AreEqual(0, references.GetArrayLength(), "Should not find any references");
        }

        [TestMethod]
        public async Task FindUsages_InterfaceImplementation_ShouldFindImplementations()
        {
            // Arrange
            var testCode = @"
namespace TestNamespace
{
    public interface ITestInterface
    {
        void DoWork();
        string GetResult();
    }
    
    public class TestImplementation : ITestInterface
    {
        public void DoWork()
        {
            Console.WriteLine(""Working..."");
        }
        
        public string GetResult()
        {
            return ""Result"";
        }
    }
    
    public class AnotherImplementation : ITestInterface
    {
        public void DoWork()
        {
            Console.WriteLine(""Another work..."");
        }
        
        public string GetResult()
        {
            return ""Another result"";
        }
    }
    
    public class Consumer
    {
        public void UseInterface(ITestInterface instance)
        {
            instance.DoWork();
            var result = instance.GetResult();
        }
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
            var result = await AnalysisTools.FindUsages(
                _workspaceFactory,
                NullLogger<AnalysisToolsLogCategory>.Instance,
                projectPath,
                "ITestInterface",
                maxResults: 50,
                includePrivateMembers: true,
                includeInheritedMembers: true, // インターフェースの実装を含める
                includeExternalSymbols: false,
                searchMode: "declaration",
                CancellationToken.None
            );

            // Assert
            Assert.IsNotNull(result);
            
            var jsonResult = result.ToString();
            Assert.IsNotNull(jsonResult);
            var parsedJson = JsonDocument.Parse(jsonResult);
            var root = parsedJson.RootElement;
            
            // 検索語の確認
            Assert.AreEqual("ITestInterface", root.GetProperty("searchTerm").GetString());
            
            // 見つかったシンボルの確認
            var symbolsFound = root.GetProperty("symbolsFound");
            Assert.IsTrue(symbolsFound.GetArrayLength() > 0, "Should find at least one symbol");
            
            var firstSymbol = symbolsFound.EnumerateArray().First();
            Assert.AreEqual("ITestInterface", firstSymbol.GetProperty("name").GetString());
            Assert.AreEqual("NamedType", firstSymbol.GetProperty("kind").GetString());
            
            // 参照の確認
            var references = root.GetProperty("references");
            Assert.IsTrue(references.GetArrayLength() > 0, "Should find interface usages");
            
            // インターフェース使用箇所の確認
            var foundImplementation = false;
            var foundParameter = false;
            
            foreach (var fileRef in references.EnumerateArray())
            {
                var locations = fileRef.GetProperty("locations");
                foreach (var location in locations.EnumerateArray())
                {
                    var context = location.GetProperty("context").GetString();
                    if (context != null && context.Contains(": ITestInterface"))
                    {
                        foundImplementation = true;
                    }
                    if (context != null && context.Contains("ITestInterface instance"))
                    {
                        foundParameter = true;
                    }
                }
            }
            
            Assert.IsTrue(foundImplementation, "Should find interface implementations");
            Assert.IsTrue(foundParameter, "Should find interface as parameter type");
        }
    }
}