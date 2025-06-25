using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpTools.Tools.Mcp.Tools;
using SharpTools.Tools.Extensions;
using SharpTools.Tools.Interfaces;
using SharpTools.Tools.Services;
using System.IO;

namespace TestConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // サービスプロバイダーの構築
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.WithSharpToolsServices();
            
            var serviceProvider = services.BuildServiceProvider();
            
            Console.WriteLine("=== SharpTools GetMethodSignature オーバーロードテスト ===\n");
            
            // テストプロジェクトのパス
            var testPath = @"/mnt/c/Users/info/source/repos/zio3/SharpToolsMCP/TestProject/";
            
            try
            {
                // GetMethodSignatureのオーバーロードテスト
                Console.WriteLine("【GetMethodSignatureオーバーロードテスト】");
                await TestGetMethodSignatureOverloads(serviceProvider, testPath);
                
                Console.WriteLine("\n=== テストが完了しました ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nエラーが発生しました: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            
            Console.WriteLine("\nテスト完了");
        }
        
        static async Task TestOverloadMethodIdentification(IServiceProvider serviceProvider, string basePath)
        {
            var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
            var modificationService = serviceProvider.GetRequiredService<ICodeModificationService>();
            var codeAnalysisService = serviceProvider.GetRequiredService<ICodeAnalysisService>();
            var logger = serviceProvider.GetRequiredService<ILogger<ModificationToolsLogCategory>>();
            
            var filePath = Path.Combine(basePath, "TestClass.cs");
            
            // Process(int)のみを更新
            Console.WriteLine("- Process(int)メソッドを更新中...");
            
            var result = await ModificationTools.OverwriteMember(
                workspaceFactory,
                modificationService,
                codeAnalysisService,
                logger,
                filePath,
                "Process(int)",
                @"string Process(int input)
        {
            return $""Integer Updated: {input * 10}"";
        }");
            
            Console.WriteLine($"結果: {result.Substring(0, Math.Min(result.Length, 100))}...");
            
            // ファイルを読み込んで確認
            var content = await File.ReadAllTextAsync(filePath);
            var hasStringVersion = content.Contains("String: {input}");
            var hasUpdatedIntVersion = content.Contains("Integer Updated:");
            
            Console.WriteLine($"✓ Process(string)は変更されていない: {hasStringVersion}");
            Console.WriteLine($"✓ Process(int)が更新された: {hasUpdatedIntVersion}");
        }
        
        static async Task TestAccessModifierInheritance(IServiceProvider serviceProvider, string basePath)
        {
            var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
            var modificationService = serviceProvider.GetRequiredService<ICodeModificationService>();
            var codeAnalysisService = serviceProvider.GetRequiredService<ICodeAnalysisService>();
            var logger = serviceProvider.GetRequiredService<ILogger<ModificationToolsLogCategory>>();
            
            var filePath = Path.Combine(basePath, "TestClass.cs");
            
            // publicを省略してメソッドを更新
            Console.WriteLine("- publicを省略してTestMethodを更新中...");
            
            var result = await ModificationTools.OverwriteMember(
                workspaceFactory,
                modificationService,
                codeAnalysisService,
                logger,
                filePath,
                "TestMethod",
                @"string TestMethod(string input)
        {
            return $""アクセス修飾子自動継承テスト: {input}"";
        }");
            
            Console.WriteLine($"結果: {result.Substring(0, Math.Min(result.Length, 100))}...");
            
            // ファイルを読み込んで確認
            var content = await File.ReadAllTextAsync(filePath);
            Console.WriteLine($"✓ publicが自動的に継承されているはず");
        }
        
        static async Task TestIdentifierFormats(IServiceProvider serviceProvider, string basePath)
        {
            var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
            var modificationService = serviceProvider.GetRequiredService<ICodeModificationService>();
            var codeAnalysisService = serviceProvider.GetRequiredService<ICodeAnalysisService>();
            var logger = serviceProvider.GetRequiredService<ILogger<ModificationToolsLogCategory>>();
            
            var filePath = Path.Combine(basePath, "TestClass.cs");
            
            // 完全修飾名での指定
            Console.WriteLine("- 完全修飾名でProcess(string)を更新中...");
            
            try
            {
                // まず、いくつかの形式を試してみる
                var testFormats = new[] {
                    "TestProject.TestClass.Process(System.String)",
                    "TestClass.Process(System.String)",
                    "Process(System.String)",
                    "TestProject.TestClass.Process(string)",
                    "TestClass.Process(string)"
                };
                
                foreach (var format in testFormats)
                {
                    try
                    {
                        Console.WriteLine($"  試行中: {format}");
                        var result = await ModificationTools.OverwriteMember(
                            workspaceFactory,
                            modificationService,
                            codeAnalysisService,
                            logger,
                            filePath,
                            format,
                            @"public string Process(string input)
        {
            return $""String with Full Qualification: {input}"";
        }");
                        
                        Console.WriteLine($"    ✓ 成功: {format}");
                        break; // 成功したら終了
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    ✗ 失敗: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 完全修飾名での指定: 失敗 - {ex.Message}");
            }
        }
        
        static async Task TestGetMethodSignatureOverloads(IServiceProvider serviceProvider, string basePath)
        {
            var workspaceFactory = serviceProvider.GetRequiredService<StatelessWorkspaceFactory>();
            var codeAnalysisService = serviceProvider.GetRequiredService<ICodeAnalysisService>();
            var fuzzyFqnLookupService = serviceProvider.GetRequiredService<IFuzzyFqnLookupService>();
            var logger = serviceProvider.GetRequiredService<ILogger<AnalysisToolsLogCategory>>();
            
            var contextPath = Path.Combine(basePath, "TestProject.csproj");
            
            // テスト1: パラメータ指定なし
            Console.WriteLine("\n1. パラメータ指定なしでProcessを検索:");
            try
            {
                var result = await AnalysisTools.GetMethodSignature(
                    workspaceFactory,
                    codeAnalysisService,
                    fuzzyFqnLookupService,
                    logger,
                    contextPath,
                    "TestProject.OverloadTest.Process");
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  エラー: {ex.Message}");
            }
            
            // テスト2: System.String指定
            Console.WriteLine("\n2. System.StringパラメータでProcessを検索:");
            try
            {
                var result = await AnalysisTools.GetMethodSignature(
                    workspaceFactory,
                    codeAnalysisService,
                    fuzzyFqnLookupService,
                    logger,
                    contextPath,
                    "TestProject.OverloadTest.Process(System.String)");
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  エラー: {ex.Message}");
            }
            
            // テスト3: System.Int32指定
            Console.WriteLine("\n3. System.Int32パラメータでProcessを検索:");
            try
            {
                var result = await AnalysisTools.GetMethodSignature(
                    workspaceFactory,
                    codeAnalysisService,
                    fuzzyFqnLookupService,
                    logger,
                    contextPath,
                    "TestProject.OverloadTest.Process(System.Int32)");
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  エラー: {ex.Message}");
            }
            
            // テスト4: System.Double指定
            Console.WriteLine("\n4. System.DoubleパラメータでProcessを検索:");
            try
            {
                var result = await AnalysisTools.GetMethodSignature(
                    workspaceFactory,
                    codeAnalysisService,
                    fuzzyFqnLookupService,
                    logger,
                    contextPath,
                    "TestProject.OverloadTest.Process(System.Double)");
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  エラー: {ex.Message}");
            }
        }
    }
}