using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Services;
using SharpTools.Tools.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;

namespace SharpTools.Tests
{
[TestClass]
public class GetMethodSignatureBugTests
{
private string _testDataDirectory;
private string _tempDirectory;
private StatelessWorkspaceFactory _workspaceFactory;
private ICodeAnalysisService _codeAnalysisService;
private IFuzzyFqnLookupService _fuzzyFqnLookupService;

[TestInitialize]
public void Setup()
{
_testDataDirectory = Path.Combine(TestContext.TestRunDirectory!, "TestData");
_tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{Guid.NewGuid()}");
Directory.CreateDirectory(_tempDirectory);

// テストデータをコピー
CopyTestData();

var projectDiscovery = new ProjectDiscoveryService();
_workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, projectDiscovery);
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

public TestContext TestContext { get; set; } = null!;

private void CopyTestData()
{
var sourceDataDir = Path.Combine(TestContext.TestRunDirectory!, "..", "..", "..", "TestData");
if (!Directory.Exists(sourceDataDir))
{
sourceDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
}

if (Directory.Exists(sourceDataDir))
{
CopyDirectory(sourceDataDir, _testDataDirectory);
}
else
{
Directory.CreateDirectory(_testDataDirectory);
CreateTestDataFiles();
}
}

private void CreateTestDataFiles()
{
// テスト用プロジェクトファイルを作成
var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
File.WriteAllText(Path.Combine(_testDataDirectory, "TestData.csproj"), projectContent);

var overloadTestContent = @"using System;

namespace SharpTools.Tests.TestData
{
    public class OverloadTestClass
    {
        /// <summary>
        /// 文字列を処理するメソッド
        /// </summary>
        public string Process(string input)
        {
            return $""String: {input}"";
        }

        /// <summary>
        /// 整数を処理するメソッド
        /// </summary>
        public string Process(int input)
        {
            return $""Int: {input}"";
        }

        /// <summary>
        /// 浮動小数点数を処理するメソッド
        /// </summary>
        public string Process(double input)
        {
            return $""Double: {input}"";
        }
    }
}";
File.WriteAllText(Path.Combine(_testDataDirectory, "OverloadTestClass.cs"), overloadTestContent);
}

private static void CopyDirectory(string sourceDir, string destinationDir)
{
Directory.CreateDirectory(destinationDir);

foreach (string file in Directory.GetFiles(sourceDir))
{
string fileName = Path.GetFileName(file);
string destFile = Path.Combine(destinationDir, fileName);
File.Copy(file, destFile, true);
}

foreach (string dir in Directory.GetDirectories(sourceDir))
{
string dirName = Path.GetFileName(dir);
string destDir = Path.Combine(destinationDir, dirName);
CopyDirectory(dir, destDir);
}
}

/// <summary>
/// バグ確認: System.Int32指定で正確にint版が返されるか
/// </summary>
[TestMethod]
public async Task GetMethodSignature_WithSystemInt32_ShouldReturnIntVersion()
{
// Arrange
var projectFile = Path.Combine(_testDataDirectory, "TestData.csproj");
var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

// Act - プロジェクトファイルを指定
var result = await AnalysisTools.GetMethodSignature(
_workspaceFactory,
_codeAnalysisService,
_fuzzyFqnLookupService,
logger,
projectFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.Int32)",
CancellationToken.None);

// Assert
var resultJson = result?.ToString() ?? "";
Console.WriteLine($"Result for System.Int32: {resultJson}");

// int版のメソッドが返されているかチェック
Assert.IsTrue(resultJson.Contains("Process(int input)"), 
$"Expected int version but got: {resultJson}");

// 間違ってdouble版が返されていないかチェック
Assert.IsFalse(resultJson.Contains("Process(double input)"), 
$"Should not return double version but got: {resultJson}");
}

/// <summary>
/// バグ確認: System.String指定で正確にstring版が返されるか
/// </summary>
[TestMethod]
public async Task GetMethodSignature_WithSystemString_ShouldReturnStringVersion()
{
// Arrange
var projectFile = Path.Combine(_testDataDirectory, "TestData.csproj");
var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

// Act - プロジェクトファイルを指定
var result = await AnalysisTools.GetMethodSignature(
_workspaceFactory,
_codeAnalysisService,
_fuzzyFqnLookupService,
logger,
projectFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.String)",
CancellationToken.None);

// Assert
var resultJson = result?.ToString() ?? "";
Console.WriteLine($"Result for System.String: {resultJson}");

// string版のメソッドが返されているかチェック
Assert.IsTrue(resultJson.Contains("Process(string input)"), 
$"Expected string version but got: {resultJson}");

// 間違ってdouble版が返されていないかチェック
Assert.IsFalse(resultJson.Contains("Process(double input)"), 
$"Should not return double version but got: {resultJson}");
}

/// <summary>
/// バグ確認: System.Double指定でdouble版が返されるか
/// </summary>
[TestMethod]
public async Task GetMethodSignature_WithSystemDouble_ShouldReturnDoubleVersion()
{
// Arrange
var projectFile = Path.Combine(_testDataDirectory, "TestData.csproj");
var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

// Act - プロジェクトファイルを指定
var result = await AnalysisTools.GetMethodSignature(
_workspaceFactory,
_codeAnalysisService,
_fuzzyFqnLookupService,
logger,
projectFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.Double)",
CancellationToken.None);

// Assert
var resultJson = result?.ToString() ?? "";
Console.WriteLine($"Result for System.Double: {resultJson}");

// double版のメソッドが返されているかチェック
Assert.IsTrue(resultJson.Contains("Process(double input)"), 
$"Expected double version but got: {resultJson}");
}

/// <summary>
/// バグ確認: 異なるパラメータ指定で異なるメソッドが返されるか
/// </summary>
[TestMethod]
public async Task GetMethodSignature_DifferentParameters_ShouldReturnDifferentMethods()
{
// Arrange
var projectFile = Path.Combine(_testDataDirectory, "TestData.csproj");
var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

// Act - プロジェクトファイルを指定
var stringResult = await AnalysisTools.GetMethodSignature(
_workspaceFactory,
_codeAnalysisService,
_fuzzyFqnLookupService,
logger,
projectFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.String)",
CancellationToken.None);

var intResult = await AnalysisTools.GetMethodSignature(
_workspaceFactory,
_codeAnalysisService,
_fuzzyFqnLookupService,
logger,
projectFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.Int32)",
CancellationToken.None);

var doubleResult = await AnalysisTools.GetMethodSignature(
_workspaceFactory,
_codeAnalysisService,
_fuzzyFqnLookupService,
logger,
projectFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.Double)",
CancellationToken.None);

// Convert to strings for comparison
var stringResultJson = stringResult?.ToString() ?? "";
var intResultJson = intResult?.ToString() ?? "";
var doubleResultJson = doubleResult?.ToString() ?? "";

// Assert
Console.WriteLine($"String result: {stringResultJson}");
Console.WriteLine($"Int result: {intResultJson}");
Console.WriteLine($"Double result: {doubleResultJson}");

// 3つの結果がすべて異なることを確認
Assert.AreNotEqual(stringResultJson, intResultJson, "String and Int results should be different");
Assert.AreNotEqual(stringResultJson, doubleResultJson, "String and Double results should be different");
Assert.AreNotEqual(intResultJson, doubleResultJson, "Int and Double results should be different");

// それぞれが正しいバージョンを返していることを確認
Assert.IsTrue(stringResultJson.Contains("Process(string input)"));
Assert.IsTrue(intResultJson.Contains("Process(int input)"));
Assert.IsTrue(doubleResultJson.Contains("Process(double input)"));
}
}
}