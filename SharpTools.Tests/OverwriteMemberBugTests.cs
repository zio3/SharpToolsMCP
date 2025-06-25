using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
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
public class OverwriteMemberBugTests
{
private string _testDataDirectory;
private string _tempDirectory;
private ILogger<ModificationToolsLogCategory> _logger;
private ILogger<AnalysisToolsLogCategory> _analysisLogger;
private StatelessWorkspaceFactory _workspaceFactory;
private Mock<ICodeModificationService> _mockModificationService;
private Mock<ICodeAnalysisService> _mockAnalysisService;
private ICodeAnalysisService _codeAnalysisService;
private IFuzzyFqnLookupService _fuzzyFqnLookupService;

[TestInitialize]
public void Setup()
{
// テストデータディレクトリの設定
_testDataDirectory = Path.Combine(TestContext.TestRunDirectory!, "TestData");

// 一時ディレクトリの作成
_tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{Guid.NewGuid()}");
Directory.CreateDirectory(_tempDirectory);

// テストデータをコピー
CopyTestData();

// モックとサービスの設定
_logger = NullLogger<ModificationToolsLogCategory>.Instance;
_analysisLogger = NullLogger<AnalysisToolsLogCategory>.Instance;
var projectDiscovery = new ProjectDiscoveryService();
_workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, projectDiscovery);

_mockModificationService = new Mock<ICodeModificationService>();
_mockAnalysisService = new Mock<ICodeAnalysisService>();
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
// 代替パスを試す
sourceDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
}

if (Directory.Exists(sourceDataDir))
{
CopyDirectory(sourceDataDir, _testDataDirectory);
}
else
{
// テストデータファイルを直接作成
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
using System.Threading.Tasks;

namespace SharpTools.Tests.TestData
{
    public class OverloadTestClass
    {
        public string Process(string input)
        {
            return $""String: {input}"";
        }

        public string Process(int input)
        {
            return $""Int: {input}"";
        }

        public string Process(double input)
        {
            return $""Double: {input}"";
        }
    }
}";
File.WriteAllText(Path.Combine(_testDataDirectory, "OverloadTestClass.cs"), overloadTestContent);

var accessModifierTestContent = @"using System;
using System.Threading.Tasks;

namespace SharpTools.Tests.TestData
{
    public class AccessModifierTestClass
    {
        public string PublicMethod()
        {
            return ""Public"";
        }

        private string PrivateMethod()
        {
            return ""Private"";
        }

        public static string StaticMethod()
        {
            return ""Static"";
        }
    }
}";
File.WriteAllText(Path.Combine(_testDataDirectory, "AccessModifierTestClass.cs"), accessModifierTestContent);
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
/// バグ #1: GetMethodSignatureでのオーバーロード識別不良のテスト
/// 具体的なパラメータ指定で正確なメソッドが返されるかテスト
/// </summary>
[TestMethod]
public async Task GetMethodSignature_OverloadIdentification_ShouldReturnCorrectMethod()
{
// Arrange
var projectFile = Path.Combine(_testDataDirectory, "TestData.csproj");

// Act & Assert - Int32パラメータの正確な識別
try
{
var result = await AnalysisTools.GetMethodSignature(
_workspaceFactory,
_codeAnalysisService,
_fuzzyFqnLookupService,
_analysisLogger,
projectFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.Int32)",
CancellationToken.None);

// Int版のメソッドが正確に返されることを確認
Assert.IsTrue(result.Contains("Process(int input)"), 
$"Expected int version but got: {result}");
Assert.IsTrue(result.Contains("整数を処理するメソッド") || result.Contains("Int:"), 
$"Expected int version documentation but got: {result}");
}
catch (Exception ex)
{
Assert.Fail($"GetMethodSignature failed for Int32 parameter: {ex.Message}");
}
}

/// <summary>
/// バグ #2: OverwriteMemberでのオーバーロード誤処理のテスト
/// 間違ったメソッドが更新されないかテスト
/// </summary>
[TestMethod]
public async Task OverwriteMember_OverloadAccuracy_ShouldUpdateCorrectMethod()
{
// Arrange
var testFile = Path.Combine(_tempDirectory, "OverloadTestClass.cs");
var testProjectFile = Path.Combine(_tempDirectory, "TestData.csproj");
File.Copy(Path.Combine(_testDataDirectory, "OverloadTestClass.cs"), testFile, true);
File.Copy(Path.Combine(_testDataDirectory, "TestData.csproj"), testProjectFile, true);

var newMethodCode = @"/// <summary>
/// 整数を処理するメソッド - テスト更新版
/// </summary>
/// <param name=""input"">入力整数</param>
/// <returns>処理結果</returns>
string Process(int input)
{
return $""Updated Int: {input * 2}"";
}";

// Act
try
{
var result = await ModificationTools.OverwriteMember(
_workspaceFactory,
_mockModificationService.Object,
_mockAnalysisService.Object,
_logger,
testFile,
"SharpTools.Tests.TestData.OverloadTestClass.Process(System.Int32)",
newMethodCode,
CancellationToken.None);

// Assert - 正常に完了したことを確認
Assert.IsTrue(result.Contains("正常に置換しました"), 
$"Expected successful replacement but got: {result}");

// ファイル内容を確認して、正しいメソッドが更新されたかチェック
var updatedContent = File.ReadAllText(testFile);

// Int版が更新されていることを確認
Assert.IsTrue(updatedContent.Contains("Updated Int: {input * 2}"), 
"Int version should be updated");

// String版が影響を受けていないことを確認
Assert.IsTrue(updatedContent.Contains("String: {input}"), 
"String version should remain unchanged");

// 重複メソッドがないことを確認（コンパイルエラーがない）
Assert.IsFalse(result.Contains("Compilation errors detected"), 
$"Should not have compilation errors but got: {result}");
}
catch (Exception ex)
{
Assert.Fail($"OverwriteMember failed: {ex.Message}");
}
}

/// <summary>
/// バグ #3: コード構造破損のテスト
/// 無効なコードが生成されないかテスト
/// </summary>
[TestMethod]
public async Task OverwriteMember_CodeStructure_ShouldNotGenerateInvalidCode()
{
// Arrange
var testFile = Path.Combine(_tempDirectory, "AccessModifierTestClass.cs");
var testProjectFile = Path.Combine(_tempDirectory, "TestData.csproj");
File.Copy(Path.Combine(_testDataDirectory, "AccessModifierTestClass.cs"), testFile, true);
File.Copy(Path.Combine(_testDataDirectory, "TestData.csproj"), testProjectFile, true);

var newMethodCode = @"/// <summary>
/// publicメソッド - テスト更新版
/// </summary>
string PublicMethod()
{
return ""Updated Public"";
}";

// Act
try
{
var result = await ModificationTools.OverwriteMember(
_workspaceFactory,
_mockModificationService.Object,
_mockAnalysisService.Object,
_logger,
testFile,
"PublicMethod",
newMethodCode,
CancellationToken.None);

// Assert - ファイル内容を確認
var updatedContent = File.ReadAllText(testFile);

// 無効なコード構造（public /// <summary>...）が生成されていないことを確認
Assert.IsFalse(updatedContent.Contains("public /// <summary>"), 
"Should not generate invalid code structure like 'public /// <summary>'");

// 適切なアクセス修飾子が保持されていることを確認
Assert.IsTrue(updatedContent.Contains("public string PublicMethod()") || 
updatedContent.Contains("public\nstring PublicMethod()") ||
updatedContent.Contains("public\r\nstring PublicMethod()"), 
$"Public access modifier should be preserved. Content: {updatedContent}");
}
catch (Exception ex)
{
Assert.Fail($"OverwriteMember failed: {ex.Message}");
}
}

/// <summary>
/// バグ #4: アクセス修飾子の自動継承テスト
/// 元のアクセス修飾子が適切に継承されるかテスト
/// </summary>
[TestMethod]
public async Task OverwriteMember_AccessModifierInheritance_ShouldPreserveOriginalModifiers()
{
// Arrange
var testFile = Path.Combine(_tempDirectory, "AccessModifierTestClass.cs");
var testProjectFile = Path.Combine(_tempDirectory, "TestData.csproj");
File.Copy(Path.Combine(_testDataDirectory, "AccessModifierTestClass.cs"), testFile, true);
File.Copy(Path.Combine(_testDataDirectory, "TestData.csproj"), testProjectFile, true);

var newMethodCode = @"/// <summary>
/// privateメソッド - テスト更新版  
/// </summary>
string PrivateMethod()
{
return ""Updated Private"";
}";

// Act
try
{
var result = await ModificationTools.OverwriteMember(
_workspaceFactory,
_mockModificationService.Object,
_mockAnalysisService.Object,
_logger,
testFile,
"PrivateMethod",
newMethodCode,
CancellationToken.None);

// Assert
var updatedContent = File.ReadAllText(testFile);

// privateアクセス修飾子が保持されていることを確認
Assert.IsTrue(updatedContent.Contains("private string PrivateMethod()") ||
updatedContent.Contains("private\nstring PrivateMethod()") ||
updatedContent.Contains("private\r\nstring PrivateMethod()"), 
$"Private access modifier should be preserved. Content: {updatedContent}");
}
catch (Exception ex)
{
Assert.Fail($"OverwriteMember failed: {ex.Message}");
}
}

/// <summary>
/// バグ #5: シンボル識別不良のテスト
/// 簡単なメソッド名での識別が機能するかテスト
/// </summary>
[TestMethod]
public async Task OverwriteMember_SimpleMethodName_ShouldWork()
{
// Arrange
var testFile = Path.Combine(_tempDirectory, "AccessModifierTestClass.cs");
var testProjectFile = Path.Combine(_tempDirectory, "TestData.csproj");
File.Copy(Path.Combine(_testDataDirectory, "AccessModifierTestClass.cs"), testFile, true);
File.Copy(Path.Combine(_testDataDirectory, "TestData.csproj"), testProjectFile, true);

var newMethodCode = @"/// <summary>
/// staticメソッド - 簡単名テスト更新版
/// </summary>
string StaticMethod()
{
return ""Updated Static"";
}";

// Act & Assert
try
{
var result = await ModificationTools.OverwriteMember(
_workspaceFactory,
_mockModificationService.Object,
_mockAnalysisService.Object,
_logger,
testFile,
"StaticMethod",
newMethodCode,
CancellationToken.None);

// 成功することを確認
Assert.IsTrue(result.Contains("正常に置換しました"), 
$"Expected successful replacement with simple method name but got: {result}");
}
catch (Exception ex)
{
// このテストでは、簡単なメソッド名が機能しない場合は失敗とマーク
// ただし、現在のバグ状況によってはスキップする可能性
Assert.Fail($"Simple method name identification failed: {ex.Message}");
}
}

/// <summary>
/// エッジケース: 不完全なメソッド仕様の安全性チェック
/// </summary>
[TestMethod]
public async Task OverwriteMember_IncompleteMethodSpec_ShouldThrowSafetyWarning()
{
// Arrange
var testFile = Path.Combine(_tempDirectory, "AccessModifierTestClass.cs");
var testProjectFile = Path.Combine(_tempDirectory, "TestData.csproj");
File.Copy(Path.Combine(_testDataDirectory, "AccessModifierTestClass.cs"), testFile, true);
File.Copy(Path.Combine(_testDataDirectory, "TestData.csproj"), testProjectFile, true);

// 不完全なメソッド仕様（本体なし）
var incompleteMethodCode = @"string PublicMethod()";

// Act & Assert
try
{
var result = await ModificationTools.OverwriteMember(
_workspaceFactory,
_mockModificationService.Object,
_mockAnalysisService.Object,
_logger,
testFile,
"PublicMethod",
incompleteMethodCode,
CancellationToken.None);

// 安全性警告が表示されることを期待
Assert.Fail("Expected safety warning for incomplete method specification");
}
catch (Exception ex)
{
// 安全性警告が含まれていることを確認
Assert.IsTrue(ex.Message.Contains("SAFETY WARNING") || 
ex.Message.Contains("incomplete") ||
ex.Message.Contains("構文エラー"), 
$"Expected safety warning but got: {ex.Message}");
}
}
}
}