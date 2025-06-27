using System;
using System.Collections.Generic;
using System.Linq;

namespace TestNamespace
{
    public class SuffixSearchExample 
    {
        // シンプルな後方一致検索のサンプル実装
        public static List<ISymbol> FindByEndsWith(IEnumerable<ISymbol> allSymbols, string userInput)
        {
            var matches = new List<ISymbol>();
            
            foreach (var symbol in allSymbols)
            {
                string fqn = GetFullyQualifiedName(symbol);
                
                // 後方一致チェック
                if (fqn.EndsWith(userInput, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(symbol);
                }
            }
            
            return matches.OrderBy(s => GetFullyQualifiedName(s).Length).ToList(); // 短い方を優先
        }
        
        public static string GetFullyQualifiedName(ISymbol symbol)
        {
            // FuzzyFqnLookupService.GetSearchableString(symbol) の代わり
            return symbol.ToDisplayString();
        }
    }
    
    // テストケース
    public class TestCases
    {
        // 入力: "ProcessData" 
        // マッチ: "TestNamespace.TestClass.ProcessData"
        
        // 入力: "TestClass.ProcessData"
        // マッチ: "TestNamespace.TestClass.ProcessData" 
        
        // 入力: "SomeNamespace.TestClass.ProcessData"
        // マッチ: "RootNamespace.SomeNamespace.TestClass.ProcessData"
    }
}
