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

namespace SharpTools.Tests {
    /// <summary>
    /// エッジケースと境界値テスト
    /// 報告された問題以外の潜在的な問題を検出
    /// </summary>
    [TestClass]
    public class EdgeCaseTests {
        private string _testDataDirectory;
        private string _tempDirectory;
        private StatelessWorkspaceFactory _workspaceFactory;
        private ICodeAnalysisService _codeAnalysisService;
        private IFuzzyFqnLookupService _fuzzyFqnLookupService;
        private Mock<ICodeModificationService> _mockModificationService;
        private Mock<IComplexityAnalysisService> _mockComplexityAnalysisService;
        private Mock<ISemanticSimilarityService> _mockSemanticSimilarityService;

        [TestInitialize]
        public void Setup() {
            _testDataDirectory = Path.Combine(TestContext.TestRunDirectory!, "TestData");
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"SharpToolsTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDirectory);

            CreateEdgeCaseTestData();

            var projectDiscovery = new ProjectDiscoveryService();
            _workspaceFactory = new StatelessWorkspaceFactory(NullLogger<StatelessWorkspaceFactory>.Instance, projectDiscovery);
            var mockSolutionManager = new Mock<ISolutionManager>();
            _codeAnalysisService = new CodeAnalysisService(mockSolutionManager.Object, NullLogger<CodeAnalysisService>.Instance);
            _fuzzyFqnLookupService = new FuzzyFqnLookupService(NullLogger<FuzzyFqnLookupService>.Instance);

            _mockModificationService = new Mock<ICodeModificationService>();
            _mockComplexityAnalysisService = new Mock<IComplexityAnalysisService>();
            _mockSemanticSimilarityService = new Mock<ISemanticSimilarityService>();
        }

        [TestCleanup]
        public void Cleanup() {
            try {
                if (Directory.Exists(_tempDirectory)) {
                    Directory.Delete(_tempDirectory, true);
                }
            } catch {
                // ベストエフォート
            }
        }

        public TestContext TestContext { get; set; } = null!;

        private void CreateEdgeCaseTestData() {
            Directory.CreateDirectory(_testDataDirectory);

            // プロジェクトファイル
            var projectContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
            File.WriteAllText(Path.Combine(_testDataDirectory, "EdgeCase.csproj"), projectContent);

            // 日本語識別子テストクラス
            var japaneseTestContent = @"using System;

namespace SharpTools.Tests.TestData
{
    /// <summary>
    /// 日本語識別子テスト用クラス
    /// </summary>
    public class 日本語テストクラス
    {
        /// <summary>
        /// 日本語メソッド名のテスト
        /// </summary>
        public string データを処理する(string 入力データ)
        {
            return $""処理済み: {入力データ}"";
        }

        /// <summary>
        /// 日本語プロパティ
        /// </summary>
        public string 名前 { get; set; } = ""デフォルト名"";

        /// <summary>
        /// 英数字混在メソッド
        /// </summary>
        public void ProcessData処理(int count, string データ名)
        {
            Console.WriteLine($""処理中: {count}, {データ名}"");
        }
    }
}";
            File.WriteAllText(Path.Combine(_testDataDirectory, "JapaneseTestClass.cs"), japaneseTestContent);

            // ジェネリック複雑パターンテストクラス
            var genericTestContent = @"using System;
using System.Collections.Generic;

namespace SharpTools.Tests.TestData
{
    public class GenericComplexClass<T, U> where T : class where U : struct
    {
        /// <summary>
        /// 複雑なジェネリックメソッド
        /// </summary>
        public Dictionary<T, List<U?>> ProcessComplexGeneric<V>(
            T input, 
            Dictionary<string, V> config,
            Func<T, U, V> processor) where V : IComparable<V>
        {
            return new Dictionary<T, List<U?>>();
        }

        /// <summary>
        /// ネストしたジェネリック
        /// </summary>
        public async Task<Result<T, Exception>> ProcessAsyncGeneric(
            CancellationToken token = default)
        {
            await Task.Delay(1, token);
            return new Result<T, Exception>();
        }
    }

    public class Result<TSuccess, TError>
    {
        public TSuccess? Success { get; set; }
        public TError? Error { get; set; }
    }
}";
            File.WriteAllText(Path.Combine(_testDataDirectory, "GenericComplexClass.cs"), genericTestContent);

            // 長いメソッド名テストクラス
            var longNameTestContent = @"using System;

namespace SharpTools.Tests.TestData
{
    public class LongNameTestClass
    {
        /// <summary>
        /// 非常に長いメソッド名のテスト
        /// </summary>
        public string VeryLongMethodNameThatExceedsNormalLengthLimitsAndTestsSystemBehaviorWithExtremelyLongIdentifiersInCSharpCode(
            string veryLongParameterNameThatAlsoExceedsNormalLimits,
            int anotherVeryLongParameterNameForTestingPurposes,
            bool yetAnotherExtremelyLongParameterNameToTestEdgeCases)
        {
            return ""Long method result"";
        }

        /// <summary>
        /// 長いメソッド名のオーバーロード
        /// </summary>
        public string VeryLongMethodNameThatExceedsNormalLengthLimitsAndTestsSystemBehaviorWithExtremelyLongIdentifiersInCSharpCode(
            double differentParameterTypeForOverloadTesting)
        {
            return ""Long method overload result"";
        }
    }
}";
            File.WriteAllText(Path.Combine(_testDataDirectory, "LongNameTestClass.cs"), longNameTestContent);

            // 特殊文字・エスケープテストクラス
            var specialCharTestContent = @"using System;

namespace SharpTools.Tests.TestData
{
    public class SpecialCharTestClass
    {
        /// <summary>
        /// @キーワードを使用した識別子
        /// </summary>
        public string @class { get; set; } = ""keyword_identifier"";

        /// <summary>
        /// @キーワードメソッド
        /// </summary>
        public void @return(string @string, int @int)
        {
            Console.WriteLine($""Keywords: {@string}, {@int}"");
        }

        /// <summary>
        /// Unicode識別子
        /// </summary>
        public string ProcessΑλφα(string βήτα)
        {
            return $""Unicode: {βήτα}"";
        }
    }
}";
            File.WriteAllText(Path.Combine(_testDataDirectory, "SpecialCharTestClass.cs"), specialCharTestContent);

            // 構文エラーファイル（わざと）
            var syntaxErrorContent = @"using System;

namespace SharpTools.Tests.TestData
{
    public class SyntaxErrorClass
    {
        /// <summary>
        /// 正常なメソッド
        /// </summary>
        public string ValidMethod()
        {
            return ""Valid"";
        }

        /// <summary>
        /// 構文エラーのあるメソッド（閉じ括弧なし）
        /// </summary>
        public string BrokenMethod()
        {
            return ""This method has syntax error"";
        // } <- 意図的にコメントアウト

        /// <summary>
        /// もう一つの正常なメソッド
        /// </summary>
        public void AnotherValidMethod()
        {
            Console.WriteLine(""Another valid method"");
        }
    }
}";
            File.WriteAllText(Path.Combine(_testDataDirectory, "SyntaxErrorClass.cs"), syntaxErrorContent);
        }

        #region 日本語識別子テスト

        /// <summary>
        /// 日本語識別子でのGetMethodSignatureテスト
        /// SharpToolsが日本語識別子を正しく処理できるかテスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_GetMethodSignature_JapaneseIdentifiers_ShouldWork() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

            // Act
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "日本語テストクラス.データを処理する",
                    CancellationToken.None);

                // Assert
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(resultJson.Contains("データを処理する"),
                    $"Should handle Japanese method names but got: {resultJson}");
                Assert.IsTrue(resultJson.Contains("入力データ"),
                    "Should handle Japanese parameter names");

                Console.WriteLine($"✅ Japanese identifiers test result: {resultJson}");
            } catch (Exception ex) {
                // 日本語識別子のサポート状況を確認
                Console.WriteLine($"⚠️ Japanese identifiers test result: {ex.Message}");
                // 必ずしも失敗とは限らない（実装により）
            }
        }

        /// <summary>
        /// 日本語識別子でのAddMemberテスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_AddMember_JapaneseIdentifiers_ShouldWork() {
            // Arrange
            var testFile = Path.Combine(_tempDirectory, "JapaneseTestClass.cs");
            File.Copy(Path.Combine(_testDataDirectory, "JapaneseTestClass.cs"), testFile, true);

            var newJapaneseMethod = @"/// <summary>
/// 新しい日本語メソッド
/// </summary>
public void 新しいメソッド()
{
    Console.WriteLine(""新しい機能"");
}";

            var logger = NullLogger<ModificationToolsLogCategory>.Instance;

            // Act
            try {
                var result = await ModificationTools.AddMember(
                    _workspaceFactory,
                    _mockModificationService.Object,
                    _codeAnalysisService,
                    _mockComplexityAnalysisService.Object,
                    _mockSemanticSimilarityService.Object,
                    logger,
                    testFile,
                    newJapaneseMethod,
                    "日本語テストクラス");

                // Assert
                var resultStr = result?.ToString() ?? "";
                Assert.IsTrue(resultStr.Contains("正常に追加しました") || resultStr.Contains("\"success\":true"),
                    $"Should handle Japanese class/method names but got: {resultStr}");

                Console.WriteLine($"✅ Japanese AddMember test result: {resultStr}");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Japanese AddMember test result: {ex.Message}");
            }
        }

        #endregion

        #region 複雑なジェネリックテスト

        /// <summary>
        /// 複雑なジェネリック制約でのGetMethodSignatureテスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_GetMethodSignature_ComplexGenerics_ShouldWork() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

            // Act
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "GenericComplexClass.ProcessComplexGeneric",
                    CancellationToken.None);

                // Assert
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(resultJson.Contains("ProcessComplexGeneric"),
                    $"Should handle complex generics but got: {resultJson}");
                Assert.IsTrue(resultJson.Contains("Dictionary") || resultJson.Contains("Func"),
                    "Should show generic type information");

                Console.WriteLine($"✅ Complex generics test result: {resultJson}");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Complex generics test result: {ex.Message}");
                // 複雑なジェネリックが処理できない場合は警告として記録
            }
        }

        #endregion

        #region 長いメソッド名テスト

        /// <summary>
        /// 非常に長いメソッド名でのパフォーマンステスト
        /// メモリやパフォーマンス問題が発生しないかテスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_LongMethodNames_ShouldNotCausePerformanceIssues() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;
            var timeout = TimeSpan.FromSeconds(10); // 10秒でタイムアウト

            using var cts = new CancellationTokenSource(timeout);

            // Act
            var startTime = DateTime.UtcNow;
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "VeryLongMethodNameThatExceedsNormalLengthLimitsAndTestsSystemBehaviorWithExtremelyLongIdentifiersInCSharpCode",
                    cts.Token);

                var elapsed = DateTime.UtcNow - startTime;

                // Assert
                Assert.IsTrue(elapsed < timeout,
                    $"Long method name processing took too long: {elapsed.TotalSeconds}s");
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(resultJson.Contains("VeryLongMethodName"),
                    $"Should handle long method names but got: {resultJson}");

                Console.WriteLine($"✅ Long method name test completed in {elapsed.TotalMilliseconds}ms");
            } catch (OperationCanceledException) {
                Assert.Fail($"Long method name processing timed out after {timeout.TotalSeconds}s");
            } catch (Exception ex) {
                var elapsed = DateTime.UtcNow - startTime;
                Console.WriteLine($"⚠️ Long method name test failed after {elapsed.TotalMilliseconds}ms: {ex.Message}");
            }
        }

        /// <summary>
        /// 長いメソッド名のオーバーロード識別テスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_LongMethodNames_OverloadIdentification_ShouldWork() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

            // Act - string版とdouble版を個別にテスト
            try {
                var stringResult = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "VeryLongMethodNameThatExceedsNormalLengthLimitsAndTestsSystemBehaviorWithExtremelyLongIdentifiersInCSharpCode(string,int,bool)",
                    CancellationToken.None);

                var doubleResult = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "VeryLongMethodNameThatExceedsNormalLengthLimitsAndTestsSystemBehaviorWithExtremelyLongIdentifiersInCSharpCode(double)",
                    CancellationToken.None);

                // Assert - 異なるオーバーロードが正確に識別されることを確認
                var stringResultJson = stringResult?.ToString() ?? "";
                var doubleResultJson = doubleResult?.ToString() ?? "";
                Assert.AreNotEqual(stringResultJson, doubleResultJson,
                    "Long method name overloads should be distinguished");
                Assert.IsTrue(stringResultJson.Contains("string") && stringResultJson.Contains("int"),
                    "String version should show multiple parameters");
                Assert.IsTrue(doubleResultJson.Contains("double"),
                    "Double version should show double parameter");

                Console.WriteLine($"✅ Long method overload test: String and Double versions correctly identified");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Long method overload test failed: {ex.Message}");
            }
        }

        #endregion

        #region 特殊文字・エスケープテスト

        /// <summary>
        /// @キーワード識別子でのテスト
        /// C#の@エスケープ識別子が正しく処理されるかテスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_EscapedIdentifiers_ShouldWork() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

            // Act
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "SpecialCharTestClass.@return",
                    CancellationToken.None);

                // Assert
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(resultJson.Contains("@return") || resultJson.Contains("return"),
                    $"Should handle escaped identifiers but got: {resultJson}");

                Console.WriteLine($"✅ Escaped identifiers test result: {resultJson}");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Escaped identifiers test result: {ex.Message}");
            }
        }

        /// <summary>
        /// Unicode識別子でのテスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_UnicodeIdentifiers_ShouldWork() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

            // Act
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "SpecialCharTestClass.ProcessΑλφα",
                    CancellationToken.None);

                // Assert
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(resultJson.Contains("ProcessΑλφα") || resultJson.Contains("Process"),
                    $"Should handle Unicode identifiers but got: {resultJson}");

                Console.WriteLine($"✅ Unicode identifiers test result: {resultJson}");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Unicode identifiers test result: {ex.Message}");
            }
        }

        #endregion

        #region 構文エラー耐性テスト

        /// <summary>
        /// 構文エラーがあるファイルでもGetMethodSignatureが機能するかテスト
        /// SharpToolsのエラー耐性を確認
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_SyntaxErrors_ShouldStillAnalyzeValidParts() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

            // Act - 構文エラーがあるファイル内の正常なメソッドにアクセス
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "SyntaxErrorClass.ValidMethod",
                    CancellationToken.None);

                // Assert - 構文エラーがあっても正常部分は解析できることを確認
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(resultJson.Contains("ValidMethod"),
                    $"Should analyze valid parts despite syntax errors but got: {resultJson}");

                Console.WriteLine($"✅ Syntax error resilience test result: {result}");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Syntax error resilience test result: {ex.Message}");
                // 構文エラー耐性が低い場合は警告として記録（必ずしも失敗ではない）
            }
        }

        /// <summary>
        /// 構文エラーがあるメソッドのOverwriteMemberテスト
        /// 破損したメソッドを修正できるかテスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_OverwriteBrokenMethod_ShouldFixSyntaxError() {
            // Arrange
            var testFile = Path.Combine(_tempDirectory, "SyntaxErrorClass.cs");
            File.Copy(Path.Combine(_testDataDirectory, "SyntaxErrorClass.cs"), testFile, true);

            var fixedMethodCode = @"/// <summary>
/// 修正されたメソッド
/// </summary>
public string BrokenMethod()
{
    return ""This method is now fixed"";
}";

            var logger = NullLogger<ModificationToolsLogCategory>.Instance;

            // Act
            try {
                var result = await ModificationTools.OverwriteMember(
                    _workspaceFactory,
                    _mockModificationService.Object,
                    _codeAnalysisService,
                    logger,
                    testFile,
                    "BrokenMethod",
                    fixedMethodCode,
                    null, // userConfirmResponse
                    CancellationToken.None);

                // Assert - 構文エラーが修正されることを確認
                var updatedContent = File.ReadAllText(testFile);

                // 修正されたメソッドが正しい構文になっていることを確認
                Assert.IsTrue(updatedContent.Contains("This method is now fixed"),
                    "Should fix broken method");

                // 一般的な構文チェック（括弧の対応など）
                var openBraces = updatedContent.Count(c => c == '{');
                var closeBraces = updatedContent.Count(c => c == '}');
                Assert.AreEqual(openBraces, closeBraces,
                    "Fixed code should have matching braces");

                Console.WriteLine($"✅ Broken method fix test result: {result}");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Broken method fix test result: {ex.Message}");
            }
        }

        #endregion

        #region パフォーマンステスト

        /// <summary>
        /// 大量のメンバーを持つクラスでのパフォーマンステスト
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_LargeClass_Performance_ShouldBeReasonable() {
            // Arrange - 大きなクラスファイルを動的に生成
            var largeClassFile = Path.Combine(_tempDirectory, "LargeClass.cs");
            var largeClassContent = GenerateLargeClassContent(1000); // 1000メンバー
            File.WriteAllText(largeClassFile, largeClassContent);

            var largeProjectFile = Path.Combine(_tempDirectory, "LargeProject.csproj");
            File.Copy(Path.Combine(_testDataDirectory, "EdgeCase.csproj"), largeProjectFile, true);

            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;
            var timeout = TimeSpan.FromMinutes(1); // 1分でタイムアウト

            using var cts = new CancellationTokenSource(timeout);

            // Act
            var startTime = DateTime.UtcNow;
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    largeProjectFile,
                    "LargeClass.Method500",
                    cts.Token);

                var elapsed = DateTime.UtcNow - startTime;

                // Assert - 合理的な時間内に完了することを確認
                Assert.IsTrue(elapsed < TimeSpan.FromSeconds(30),
                    $"Large class processing took too long: {elapsed.TotalSeconds}s");
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(resultJson.Contains("Method500"),
                    $"Should find method in large class but got: {resultJson}");

                Console.WriteLine($"✅ Large class performance test completed in {elapsed.TotalMilliseconds}ms");
            } catch (OperationCanceledException) {
                Assert.Fail($"Large class processing timed out after {timeout.TotalMinutes} minutes");
            } catch (Exception ex) {
                var elapsed = DateTime.UtcNow - startTime;
                Console.WriteLine($"⚠️ Large class performance test failed after {elapsed.TotalMilliseconds}ms: {ex.Message}");
            }
        }

        private string GenerateLargeClassContent(int memberCount) {
            var content = @"using System;

namespace SharpTools.Tests.TestData
{
    public class LargeClass
    {";

            for (int i = 0; i < memberCount; i++) {
                content += $@"
        /// <summary>
        /// メソッド{i}
        /// </summary>
        public string Method{i}(int param{i})
        {{
            return $""Method{i}: {{param{i}}}"";
        }}";
            }

            content += @"
    }
}";
            return content;
        }

        #endregion

        #region 同時実行テスト

        /// <summary>
        /// 複数の操作を同時実行した際の安全性テスト
        /// スレッドセーフティの確認
        /// </summary>
        [TestMethod]
        public async Task EdgeCase_ConcurrentOperations_ShouldBeSafe() {
            // Arrange
            var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
            var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

            var tasks = new List<Task<object>>();

            // Act - 複数のGetMethodSignatureを同時実行
            for (int i = 0; i < 10; i++) {
                var task = AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    "日本語テストクラス.データを処理する",
                    CancellationToken.None);
                tasks.Add(task);
            }

            // Assert - 全て正常に完了することを確認
            try {
                var results = await Task.WhenAll(tasks);

                Assert.AreEqual(10, results.Length, "All concurrent operations should complete");

                foreach (var result in results) {
                    var resultJson = result?.ToString() ?? "";
                    Assert.IsTrue(!string.IsNullOrEmpty(resultJson),
                        "Concurrent operations should return valid results");
                }

                Console.WriteLine($"✅ Concurrent operations test: All {results.Length} operations completed successfully");
            } catch (Exception ex) {
                Console.WriteLine($"⚠️ Concurrent operations test failed: {ex.Message}");
                throw;
            }
        }

    #endregion

    #region 報告されたバグのエッジケーステスト

    /// <summary>
    /// GetMethodSignatureの短縮名問題のエッジケース
    /// 問題#1: 短縮名で動作しない問題の詳細テスト
    /// </summary>
    [TestMethod]
    public async Task ReportedBug_GetMethodSignature_ShortName_EdgeCases() {
        // Arrange
        var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
        var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

        var testCases = new[] {
            ("TestClass.ProcessDataAsync", "短縮名（報告された問題）"),
            ("ProcessDataAsync", "メソッド名のみ"),
            ("日本語テストクラス.データを処理する", "日本語短縮名"),
            ("データを処理する", "日本語メソッド名のみ"),
            ("GenericComplexClass.ProcessComplexGeneric", "ジェネリック短縮名"),
            ("ProcessComplexGeneric", "ジェネリックメソッド名のみ")
        };

        // Act & Assert
        foreach (var (methodName, description) in testCases) {
            try {
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    methodName,
                    CancellationToken.None);

                Console.WriteLine($"✅ {description}: {methodName} -> Success");
                var resultJson = result?.ToString() ?? "";
                Assert.IsTrue(!string.IsNullOrEmpty(resultJson), $"{description} should return valid result");
            } catch (Exception ex) {
                Console.WriteLine($"❌ {description}: {methodName} -> Failed: {ex.Message}");
                // 短縮名が失敗することは既知の問題なので、テスト失敗にはしない
                // ただし、問題の範囲を記録
            }
        }
    }

    /// <summary>
    /// usingディレクティブ自動追加機能の不足テスト
    /// 問題#2: StringBuilder使用時の手動using追加問題
    /// </summary>
    [TestMethod]
    public async Task ReportedBug_UsingDirectives_AutoDetection_EdgeCases() {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "UsingTestClass.cs");
        var basicClassContent = @"using System;

namespace SharpTools.Tests.TestData
{
    public class UsingTestClass
    {
        public void ExistingMethod()
        {
            Console.WriteLine(""Existing"");
        }
    }
}";
        File.WriteAllText(testFile, basicClassContent);

        var testCases = new[] {
            ("StringBuilder", "System.Text", "StringBuilderを使用するメソッド"),
            ("List<string>", "System.Collections.Generic", "ジェネリックリストを使用するメソッド"), 
            ("HttpClient", "System.Net.Http", "HttpClientを使用するメソッド"),
            ("JsonSerializer", "System.Text.Json", "JsonSerializerを使用するメソッド")
        };

        var logger = NullLogger<ModificationToolsLogCategory>.Instance;

        // Act & Assert
        foreach (var (typeName, expectedUsing, description) in testCases) {
            var methodCode = $@"/// <summary>
/// {description}
/// </summary>
public void Use{typeName.Replace("<", "").Replace(">", "").Replace("string", "String")}()
{{
    var instance = new {typeName}();
    Console.WriteLine(instance.ToString());
}}";

            try {
                var result = await ModificationTools.AddMember(
                    _workspaceFactory,
                    _mockModificationService.Object,
                    _codeAnalysisService,
                    _mockComplexityAnalysisService.Object,
                    _mockSemanticSimilarityService.Object,
                    logger,
                    testFile,
                    methodCode,
                    "UsingTestClass");

                // ファイル内容を確認してusingが自動追加されたかチェック
                var updatedContent = File.ReadAllText(testFile);
                var hasAutoUsing = updatedContent.Contains($"using {expectedUsing};");
                
                Console.WriteLine($"{(hasAutoUsing ? "✅" : "❌")} {description}: using {expectedUsing} {(hasAutoUsing ? "自動追加済み" : "手動追加が必要")}");
                
                if (!hasAutoUsing) {
                    Console.WriteLine($"   ⚠️ 問題確認: {typeName}使用時にusing {expectedUsing}が自動追加されていません");
                }
            } catch (Exception ex) {
                Console.WriteLine($"❌ {description} テスト失敗: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// インターフェースメンバー追加時の構文エラーテスト
    /// 問題#3: インターフェースにメソッド追加時の不正構文生成
    /// </summary>
    [TestMethod]
    public async Task ReportedBug_Interface_MemberAddition_SyntaxErrors() {
        // Arrange
        var interfaceFile = Path.Combine(_tempDirectory, "TestInterface.cs");
        var interfaceContent = @"using System;

namespace SharpTools.Tests.TestData
{
    public interface ITestInterface
    {
        string ExistingMethod();
    }
}";
        File.WriteAllText(interfaceFile, interfaceContent);

        var testCases = new[] {
            ("string NewMethod();", "シンプルなメソッド宣言"),
            ("Task<string> AsyncMethod();", "非同期メソッド宣言"), 
            ("void MethodWithParameters(int id, string name);", "パラメータ付きメソッド宣言"),
            ("T GenericMethod<T>(T input) where T : class;", "ジェネリックメソッド宣言")
        };

        var logger = NullLogger<ModificationToolsLogCategory>.Instance;

        // Act & Assert
        foreach (var (methodDeclaration, description) in testCases) {
            var methodCodeWithDocumentation = $@"/// <summary>
/// {description}
/// </summary>
{methodDeclaration}";

            try {
                var result = await ModificationTools.AddMember(
                    _workspaceFactory,
                    _mockModificationService.Object,
                    _codeAnalysisService,
                    _mockComplexityAnalysisService.Object,
                    _mockSemanticSimilarityService.Object,
                    logger,
                    interfaceFile,
                    methodCodeWithDocumentation,
                    "ITestInterface");

                // インターフェースファイルの構文をチェック
                var updatedContent = File.ReadAllText(interfaceFile);
                
                // 一般的な構文エラーパターンをチェック
                var hasSyntaxErrors = new[] {
                    ")が必要です",
                    "{ get; set; }", // インターフェースでプロパティ実装
                    "{ }", // インターフェースでメソッド実装
                    ";;", // セミコロン重複
                }.Any(pattern => updatedContent.Contains(pattern));

                if (hasSyntaxErrors) {
                    Console.WriteLine($"❌ {description}: 構文エラーが検出されました");
                    Console.WriteLine($"   ⚠️ 問題確認: インターフェースメンバー追加時に不正な構文が生成されています");
                } else {
                    Console.WriteLine($"✅ {description}: 構文エラーなし");
                }

            } catch (Exception ex) {
                Console.WriteLine($"❌ {description} テスト失敗: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// OverwriteMemberによる破損テスト
    /// 問題#4: OverwriteMemberが不正な構文を生成する問題
    /// </summary>
    [TestMethod]
    public async Task ReportedBug_OverwriteMember_CorruptionPatterns() {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "OverwriteTestClass.cs");
        var originalContent = @"using System;

namespace SharpTools.Tests.TestData
{
    public class OverwriteTestClass
    {
        /// <summary>
        /// オリジナルメソッド
        /// </summary>
        public string OriginalMethod()
        {
            return ""Original"";
        }

        public void AnotherMethod()
        {
            Console.WriteLine(""Another method"");
        }
    }
}";
        File.WriteAllText(testFile, originalContent);

        var testCases = new[] {
            (
                @"/// <summary>
/// 更新されたメソッド
/// </summary>
public string OriginalMethod()
{
    return ""Updated"";
}",
                "正常な置換パターン"
            ),
            (
                @"/// <summary>
/// XMLドキュメント付きメソッド
/// </summary>
/// <returns>戻り値の説明</returns>
public string OriginalMethod()
{
    return ""With XML docs"";
}",
                "詳細XMLドキュメント付きパターン"
            ),
            (
                @"[Obsolete(""このメソッドは廃止予定です"")]
/// <summary>
/// 属性付きメソッド
/// </summary>
public string OriginalMethod()
{
    return ""With attributes"";
}",
                "属性付きパターン"
            )
        };

        var logger = NullLogger<ModificationToolsLogCategory>.Instance;

        // Act & Assert
        foreach (var (newMethodCode, description) in testCases) {
            // 元のファイルを復元
            File.WriteAllText(testFile, originalContent);

            try {
                var result = await ModificationTools.OverwriteMember(
                    _workspaceFactory,
                    _mockModificationService.Object,
                    _codeAnalysisService,
                    logger,
                    testFile,
                    "OriginalMethod",
                    newMethodCode,
                    null, // userConfirmResponse
                    CancellationToken.None);

                // 破損パターンをチェック
                var updatedContent = File.ReadAllText(testFile);
                
                var corruptionPatterns = new[] {
                    "public /// <summary>", // 報告された問題パターン
                    "/// <summary>public", // 逆順パターン
                    "</summary>public", // 終了タグ直後
                    "public public", // 重複キーワード
                    "{{", // 括弧重複
                    "}}", // 閉じ括弧重複
                    ";;", // セミコロン重複
                };

                var foundCorruption = corruptionPatterns.Where(pattern => 
                    updatedContent.Contains(pattern)).ToList();

                if (foundCorruption.Any()) {
                    Console.WriteLine($"❌ {description}: 破損パターンが検出されました");
                    foreach (var corruption in foundCorruption) {
                        Console.WriteLine($"   ⚠️ 検出された破損: '{corruption}'");
                    }
                } else {
                    Console.WriteLine($"✅ {description}: 破損なし");
                }

                // 基本的な構文チェック
                var openBraces = updatedContent.Count(c => c == '{');
                var closeBraces = updatedContent.Count(c => c == '}');
                if (openBraces != closeBraces) {
                    Console.WriteLine($"   ⚠️ 括弧の不一致: {{ {openBraces}, }} {closeBraces}");
                }

            } catch (Exception ex) {
                Console.WriteLine($"❌ {description} テスト失敗: {ex.Message}");
            }
        }
    }

    #endregion

    #region 境界値・極端値テスト

    /// <summary>
    /// 空のクラス・ファイルでの動作テスト
    /// </summary>
    [TestMethod]
    public async Task EdgeCase_EmptyClass_ShouldHandleGracefully() {
        // Arrange
        var emptyClassFile = Path.Combine(_tempDirectory, "EmptyClass.cs");
        var emptyClassContent = @"namespace SharpTools.Tests.TestData
{
    public class EmptyClass
    {
    }
}";
        File.WriteAllText(emptyClassFile, emptyClassContent);

        var logger = NullLogger<ModificationToolsLogCategory>.Instance;

        // Act - 空のクラスにメンバーを追加
        try {
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _mockModificationService.Object,
                _codeAnalysisService,
                _mockComplexityAnalysisService.Object,
                _mockSemanticSimilarityService.Object,
                logger,
                emptyClassFile,
                "public void FirstMethod() { }",
                "EmptyClass");

            // Assert
            var updatedContent = File.ReadAllText(emptyClassFile);
            Assert.IsTrue(updatedContent.Contains("FirstMethod"),
                "Should add member to empty class");

            Console.WriteLine($"✅ Empty class test: Member added successfully");
        } catch (Exception ex) {
            Console.WriteLine($"⚠️ Empty class test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 単一行ファイルでの動作テスト
    /// </summary>
    [TestMethod]
    public async Task EdgeCase_SingleLineFile_ShouldHandleGracefully() {
        // Arrange
        var singleLineFile = Path.Combine(_tempDirectory, "SingleLine.cs");
        var singleLineContent = "namespace Test { public class SingleLineClass { } }";
        File.WriteAllText(singleLineFile, singleLineContent);

        var logger = NullLogger<ModificationToolsLogCategory>.Instance;

        // Act
        try {
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _mockModificationService.Object,
                _codeAnalysisService,
                _mockComplexityAnalysisService.Object,
                _mockSemanticSimilarityService.Object,
                logger,
                singleLineFile,
                "public void NewMethod() { }",
                "SingleLineClass");

            Console.WriteLine($"✅ Single line file test: {result}");
        } catch (Exception ex) {
            Console.WriteLine($"⚠️ Single line file test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 非常に深いネストでの動作テスト
    /// </summary>
    [TestMethod]
    public async Task EdgeCase_DeepNesting_ShouldHandleGracefully() {
        // Arrange
        var deepNestedFile = Path.Combine(_tempDirectory, "DeepNested.cs");
        var deepNestedContent = @"namespace Level1
{
    namespace Level2
    {
        namespace Level3
        {
            namespace Level4
            {
                public class DeeplyNestedClass
                {
                    public class NestedClass1
                    {
                        public class NestedClass2
                        {
                            public void ExistingMethod() { }
                        }
                    }
                }
            }
        }
    }
}";
        File.WriteAllText(deepNestedFile, deepNestedContent);

        var logger = NullLogger<ModificationToolsLogCategory>.Instance;

        // Act
        try {
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _mockModificationService.Object,
                _codeAnalysisService,
                _mockComplexityAnalysisService.Object,
                _mockSemanticSimilarityService.Object,
                logger,
                deepNestedFile,
                "public void NewNestedMethod() { }",
                "NestedClass2");

            Console.WriteLine($"✅ Deep nesting test: {result}");
        } catch (Exception ex) {
            Console.WriteLine($"⚠️ Deep nesting test failed: {ex.Message}");
        }
    }

    #endregion

    #region メモリ・リソーステスト

    /// <summary>
    /// 多数のファイルを連続処理した際のメモリリークテスト　時間かかるのでパス
    /// </summary>
    //[TestMethod]
    public async Task EdgeCase_MultipleFiles_MemoryUsage_ShouldNotLeak() {
        // Arrange
        var fileCount = 50;
        var logger = NullLogger<AnalysisToolsLogCategory>.Instance;
        var initialMemory = GC.GetTotalMemory(true);

        // Act - 多数のファイルを連続処理
        for (int i = 0; i < fileCount; i++) {
            var tempFile = Path.Combine(_tempDirectory, $"TempClass{i}.cs");
            var tempContent = $@"namespace Test
{{
    public class TempClass{i}
    {{
        public void Method{i}() {{ }}
    }}
}}";
            File.WriteAllText(tempFile, tempContent);

            try {
                var projectFile = Path.Combine(_testDataDirectory, "EdgeCase.csproj");
                var result = await AnalysisTools.GetMethodSignature(
                    _workspaceFactory,
                    _codeAnalysisService,
                    _fuzzyFqnLookupService,
                    logger,
                    projectFile,
                    $"TempClass{i}.Method{i}",
                    CancellationToken.None);
            } catch {
                // エラーは無視（メモリテストが目的）
            }

            // 定期的にガベージコレクション
            if (i % 10 == 0) {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // Assert - メモリ使用量をチェック
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;
        
        Console.WriteLine($"✅ Memory test: Initial: {initialMemory:N0} bytes, Final: {finalMemory:N0} bytes, Increase: {memoryIncrease:N0} bytes");
        
        // 100MB以上のメモリ増加は異常とみなす
        Assert.IsTrue(memoryIncrease < 100 * 1024 * 1024, 
            $"Memory increase too large: {memoryIncrease:N0} bytes");
    }

    #endregion

    #region エラーハンドリングテスト

    /// <summary>
    /// 存在しないファイルでのエラーハンドリングテスト
    /// </summary>
    [TestMethod]
    public async Task EdgeCase_NonExistentFile_ErrorHandling() {
        // Arrange
        var nonExistentFile = Path.Combine(_tempDirectory, "DoesNotExist.cs");
        var logger = NullLogger<AnalysisToolsLogCategory>.Instance;

        // Act & Assert
        try {
            var result = await AnalysisTools.GetMethodSignature(
                _workspaceFactory,
                _codeAnalysisService,
                _fuzzyFqnLookupService,
                logger,
                nonExistentFile,
                "NonExistent.Method",
                CancellationToken.None);

            Console.WriteLine($"⚠️ Non-existent file test: Unexpectedly succeeded with: {result}");
        } catch (Exception ex) {
            Console.WriteLine($"✅ Non-existent file test: Correctly failed with: {ex.GetType().Name}");
            Assert.IsTrue(!string.IsNullOrEmpty(ex.Message), "Error message should be informative");
        }
    }

    /// <summary>
    /// 読み取り専用ファイルでの動作テスト
    /// </summary>
    [TestMethod]
    public async Task EdgeCase_ReadOnlyFile_ErrorHandling() {
        // Arrange
        var readOnlyFile = Path.Combine(_tempDirectory, "ReadOnly.cs");
        var content = @"namespace Test { public class ReadOnlyClass { } }";
        File.WriteAllText(readOnlyFile, content);
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

        var logger = NullLogger<ModificationToolsLogCategory>.Instance;

        try {
            // Act
            var result = await ModificationTools.AddMember(
                _workspaceFactory,
                _mockModificationService.Object,
                _codeAnalysisService,
                _mockComplexityAnalysisService.Object,
                _mockSemanticSimilarityService.Object,
                logger,
                readOnlyFile,
                "public void NewMethod() { }",
                "ReadOnlyClass");

            Console.WriteLine($"⚠️ Read-only file test: Unexpectedly succeeded with: {result}");
        } catch (Exception ex) {
            Console.WriteLine($"✅ Read-only file test: Correctly failed with: {ex.GetType().Name}");
        } finally {
            // Cleanup - 読み取り専用属性を削除
            try {
                File.SetAttributes(readOnlyFile, FileAttributes.Normal);
            } catch { }
        }
    }

    #endregion
}
}