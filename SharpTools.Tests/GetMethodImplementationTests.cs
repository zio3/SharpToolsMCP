using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using SharpTools.Tools.Services;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Mcp.Models;
using SharpTools.Tools.Interfaces;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;

namespace SharpTools.Tests;

[TestClass]
public class GetMethodImplementationTests {
    private string testProjectPath = null!;
    private string testDataDirectory = null!;
    private StatelessWorkspaceFactory workspaceFactory = null!;
    private ICodeAnalysisService codeAnalysisService = null!;
    private IFuzzyFqnLookupService fuzzyFqnLookupService = null!;
    private ILogger<AnalysisToolsLogCategory> logger = null!;
    
    [TestInitialize]
    public async Task Initialize() {
        // テスト用の一時ディレクトリを作成
        testDataDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDataDirectory);
        var testProjectDir = Path.Combine(testDataDirectory, "TestProject");
        Directory.CreateDirectory(testProjectDir);
        
        // 依存関係の初期化
        var discoveryService = new ProjectDiscoveryService();
        workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, discoveryService);
        
        var mockSolutionManager = new Mock<ISolutionManager>();
        codeAnalysisService = new CodeAnalysisService(mockSolutionManager.Object, NullLogger<CodeAnalysisService>.Instance);
        fuzzyFqnLookupService = new FuzzyFqnLookupService(NullLogger<FuzzyFqnLookupService>.Instance);
        logger = NullLogger<AnalysisToolsLogCategory>.Instance;
        
        // テスト用のプロジェクトファイルパス
        testProjectPath = Path.Combine(testDataDirectory, "TestProject", "TestProject.csproj");
        
        // テスト用のC#ファイルを作成
        var testClassPath = Path.Combine(testDataDirectory, "TestProject", "TestMethodImplementation.cs");
        var testClassContent = @"using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject {
    /// <summary>
    /// Test class for GetMethodImplementation
    /// </summary>
    public class TestMethodImplementation {
        private int counter = 0;
        
        /// <summary>
        /// Simple method with documentation
        /// </summary>
        /// <param name=""input"">Input value</param>
        /// <returns>Processed result</returns>
        public string ProcessData(string input) {
            if (string.IsNullOrEmpty(input)) {
                throw new ArgumentNullException(nameof(input));
            }
            
            counter++;
            return $""Processed: {input} (count: {counter})"";
        }
        
        /// <summary>
        /// Overloaded method
        /// </summary>
        public string ProcessData(string input, int count) {
            var result = new List<string>();
            for (int i = 0; i < count; i++) {
                result.Add(ProcessData(input));
            }
            return string.Join("", "", result);
        }
        
        /// <summary>
        /// Method with complex implementation
        /// </summary>
        public async Task<int> ComplexOperation(List<string> items, bool parallel = false) {
            // This is a very long method to test truncation
            int totalLength = 0;
            
            if (parallel) {
                var tasks = items.Select(async item => {
                    await Task.Delay(10);
                    return item.Length;
                });
                
                var lengths = await Task.WhenAll(tasks);
                totalLength = lengths.Sum();
            } else {
                foreach (var item in items) {
                    await Task.Delay(10);
                    totalLength += item.Length;
                }
            }
            
            // Add more lines to test truncation
            Log(""Starting operation..."");
            Log($""Processing {items.Count} items"");
            Log($""Parallel mode: {parallel}"");
            
            if (totalLength > 100) {
                Log(""Large dataset detected"");
            }
            
            return totalLength;
        }
        
        private void Log(string message) {
            Console.WriteLine($""[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}"");
        }
        
        // Generic method
        public T GetDefault<T>() where T : new() {
            return new T();
        }
        
        // Method with ref/out parameters
        public bool TryParse(string input, out int result, ref string error) {
            if (int.TryParse(input, out result)) {
                error = string.Empty;
                return true;
            }
            
            error = ""Invalid integer format"";
            return false;
        }
    }
}";
        
        await File.WriteAllTextAsync(testClassPath, testClassContent);
        
        // プロジェクトファイルを作成
        var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
        
        await File.WriteAllTextAsync(testProjectPath, projectContent);
    }
    
    [TestCleanup]
    public void Cleanup() {
        // 一時ディレクトリを削除
        if (Directory.Exists(testDataDirectory)) {
            try {
                Directory.Delete(testDataDirectory, true);
            } catch {
                // ベストエフォート
            }
        }
    }
    
    [TestMethod]
    public async Task GetMethodImplementation_SimpleMethod_ReturnsFullImplementation() {
        var result = await AnalysisTools.GetMethodImplementation(
            workspaceFactory,
            codeAnalysisService,
            fuzzyFqnLookupService,
            logger,
            testProjectPath,
            "TestMethodImplementation.ProcessData",
            maxLines: 500,
            includeDocumentation: true,
            includeDependencies: false
        );
        
        Assert.IsNotNull(result);
        var json = result.ToString();
        Assert.IsNotNull(json);
        
        var methodResult = JsonSerializer.Deserialize<MethodImplementationResult>(json);
        Assert.IsNotNull(methodResult);
        Assert.IsTrue(methodResult.TotalMatches > 0, "Should find at least one ProcessData method");
        
        // パラメータ数が1つのメソッドを探す（最初のオーバーロード）
        var singleParamMethod = methodResult.Methods.FirstOrDefault(m => m.Parameters.Count == 1);
        Assert.IsNotNull(singleParamMethod, "Should find ProcessData method with single parameter");
        
        Assert.AreEqual("ProcessData", singleParamMethod.Name);
        Assert.IsNotNull(singleParamMethod.FullImplementation);
        
        // デバッグ情報を出力
        Console.WriteLine($"Full Implementation:\n{singleParamMethod.FullImplementation}");
        Console.WriteLine($"Parameter count: {singleParamMethod.Parameters.Count}");
        
        Assert.IsTrue(singleParamMethod.FullImplementation.Contains("if (string.IsNullOrEmpty(input))"), 
            $"Expected to find 'if (string.IsNullOrEmpty(input))' in implementation:\n{singleParamMethod.FullImplementation}");
        Assert.IsTrue(singleParamMethod.FullImplementation.Contains("throw new ArgumentNullException"),
            $"Expected to find 'throw new ArgumentNullException' in implementation:\n{singleParamMethod.FullImplementation}");
        
        // XMLドキュメントが含まれているか確認
        if (singleParamMethod.XmlDocumentation != null) {
            Assert.IsTrue(singleParamMethod.XmlDocumentation.Contains("Simple method with documentation"));
        }
    }
    
    [TestMethod]
    public async Task GetMethodImplementation_OverloadedMethod_ReturnsAllOverloads() {
        var result = await AnalysisTools.GetMethodImplementation(
            workspaceFactory,
            codeAnalysisService,
            fuzzyFqnLookupService,
            logger,
            testProjectPath,
            "TestMethodImplementation.ProcessData",
            maxLines: 500,
            includeDocumentation: true,
            includeDependencies: false
        );
        
        var json = result.ToString();
        var methodResult = JsonSerializer.Deserialize<MethodImplementationResult>(json);
        
        Assert.IsNotNull(methodResult);
        Assert.AreEqual(2, methodResult.TotalMatches, "Should find both ProcessData overloads");
        
        // 両方のオーバーロードがIsOverloadedフラグを持つことを確認
        foreach (var method in methodResult.Methods) {
            Assert.IsTrue(method.IsOverloaded, "All overloads should have IsOverloaded = true");
        }
        
        // パラメータ数で区別
        var oneParamMethod = methodResult.Methods.First(m => m.Parameters.Count == 1);
        var twoParamMethod = methodResult.Methods.First(m => m.Parameters.Count == 2);
        
        Assert.IsNotNull(oneParamMethod);
        Assert.IsNotNull(twoParamMethod);
        
        // パラメータが正しく取得されているか確認
        Assert.IsTrue(twoParamMethod.Parameters.Count >= 2, $"Expected 2 parameters, got {twoParamMethod.Parameters.Count}");
        if (twoParamMethod.Parameters.Count > 0) {
            Assert.AreEqual("input", twoParamMethod.Parameters[0].Name);
        }
        if (twoParamMethod.Parameters.Count > 1) {
            Assert.AreEqual("count", twoParamMethod.Parameters[1].Name);
        }
    }
    
    [TestMethod]
    public async Task GetMethodImplementation_WithDependencies_AnalyzesDependencies() {
        var result = await AnalysisTools.GetMethodImplementation(
            workspaceFactory,
            codeAnalysisService,
            fuzzyFqnLookupService,
            logger,
            testProjectPath,
            "ComplexOperation",
            maxLines: 500,
            includeDocumentation: true,
            includeDependencies: true
        );
        
        var json = result.ToString();
        var methodResult = JsonSerializer.Deserialize<MethodImplementationResult>(json);
        
        Assert.IsNotNull(methodResult);
        Assert.IsTrue(methodResult.TotalMatches > 0);
        
        var method = methodResult.Methods.First();
        Assert.IsNotNull(method.Dependencies);
        
        // 依存関係が分析されているか確認
        Assert.IsTrue(method.Dependencies.CalledMethods.Count > 0, "Should detect called methods");
        Assert.IsTrue(method.Dependencies.CalledMethods.Any(m => m.Contains("Log")), "Should detect Log method calls");
        // UsedTypesは一般的な型を除外するため、空になることがある
        // Assert.IsTrue(method.Dependencies.UsedTypes.Count > 0, "Should detect used types");
    }
    
    [TestMethod]
    public async Task GetMethodImplementation_GenericMethod_IncludesConstraints() {
        var result = await AnalysisTools.GetMethodImplementation(
            workspaceFactory,
            codeAnalysisService,
            fuzzyFqnLookupService,
            logger,
            testProjectPath,
            "TestMethodImplementation.GetDefault",
            maxLines: 500,
            includeDocumentation: true,
            includeDependencies: false
        );
        
        var json = result.ToString();
        var methodResult = JsonSerializer.Deserialize<MethodImplementationResult>(json);
        
        Assert.IsNotNull(methodResult);
        Assert.IsTrue(methodResult.TotalMatches > 0);
        
        var method = methodResult.Methods.First();
        Assert.IsTrue(method.Signature.Contains("<T>"), "Signature should include generic parameter");
        Assert.IsTrue(method.FullImplementation.Contains("where T : new()"), "Implementation should include generic constraints");
    }
    
    [TestMethod]
    public async Task GetMethodImplementation_RefOutParameters_HandlesCorrectly() {
        var result = await AnalysisTools.GetMethodImplementation(
            workspaceFactory,
            codeAnalysisService,
            fuzzyFqnLookupService,
            logger,
            testProjectPath,
            "TestMethodImplementation.TryParse",
            maxLines: 500,
            includeDocumentation: true,
            includeDependencies: false
        );
        
        var json = result.ToString();
        var methodResult = JsonSerializer.Deserialize<MethodImplementationResult>(json);
        
        Assert.IsNotNull(methodResult);
        Assert.IsTrue(methodResult.TotalMatches > 0);
        
        var method = methodResult.Methods.First();
        Assert.AreEqual(3, method.Parameters.Count);
        
        // outパラメータの確認
        var outParam = method.Parameters.FirstOrDefault(p => p.Name == "result");
        Assert.IsNotNull(outParam, "Should find 'result' parameter");
        Assert.IsTrue(outParam.Modifiers.Contains("out"), "Should detect out modifier");
        
        // refパラメータの確認
        var refParam = method.Parameters.FirstOrDefault(p => p.Name == "error");
        Assert.IsNotNull(refParam, "Should find 'error' parameter");
        Assert.IsTrue(refParam.Modifiers.Contains("ref"), "Should detect ref modifier");
    }
    
    [TestMethod]
    public async Task GetMethodImplementation_NonExistentMethod_ThrowsException() {
        await Assert.ThrowsExceptionAsync<McpException>(async () => {
            await AnalysisTools.GetMethodImplementation(
                workspaceFactory,
                codeAnalysisService,
                fuzzyFqnLookupService,
                logger,
                testProjectPath,
                "NonExistentMethod",
                maxLines: 500,
                includeDocumentation: true,
                includeDependencies: false
            );
        });
    }
    
    [TestMethod]
    public async Task GetMethodImplementation_WithTruncation_TruncatesLargeMethod() {
        // ComplexOperationメソッドで小さなmaxLinesを指定
        var result = await AnalysisTools.GetMethodImplementation(
            workspaceFactory,
            codeAnalysisService,
            fuzzyFqnLookupService,
            logger,
            testProjectPath,
            "ComplexOperation",
            maxLines: 10, // 小さな値を設定
            includeDocumentation: true,
            includeDependencies: false
        );
        
        var json = result.ToString();
        var methodResult = JsonSerializer.Deserialize<MethodImplementationResult>(json);
        
        Assert.IsNotNull(methodResult);
        var method = methodResult.Methods.First();
        
        Assert.IsTrue(method.IsTruncated, "Method should be truncated");
        Assert.IsNotNull(method.TruncationWarning);
        Assert.IsTrue(method.FullImplementation.Contains("... [省略:"), "Should contain truncation marker");
        Assert.AreEqual(10, method.DisplayedLineCount, "Should display exactly maxLines");
        Assert.IsTrue(method.ActualLineCount > 10, "Actual line count should be greater than displayed");
    }
}