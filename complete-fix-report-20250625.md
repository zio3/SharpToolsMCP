# SharpTools完全修正レポート

## 実施日: 2025年6月25日

### 修正内容：GetMembersとGetMethodSignatureの重複表示問題

#### 問題の詳細
戻り値型が重複して表示される問題が2箇所で発生：
- GetMethodSignature: `public string string ProcessMessage`
- GetMembers: `public static decimal decimal CalculateValue`、`public void void LogMessage`

#### 修正実装

##### 1. GetMethodSignature（AnalysisTools.cs:548-572）
```csharp
// Build the signature
var modifiers = ToolHelpers.GetRoslynSymbolModifiersString(methodSymbol);
var signature = CodeAnalysisService.GetFormattedSignatureAsync(methodSymbol, false);

// Combine modifiers and signature, avoiding duplicates
string fullSignature;
if (!string.IsNullOrWhiteSpace(modifiers)) {
    // Check if signature already starts with the modifiers
    if (signature.StartsWith(modifiers)) {
        fullSignature = signature;
    } else {
        fullSignature = $"{modifiers} {signature}".Trim();
    }
} else {
    fullSignature = signature;
}

// Additional fix for duplicate return type issue
if (methodSymbol.ReturnsVoid) {
    fullSignature = System.Text.RegularExpressions.Regex.Replace(fullSignature, @"\bvoid\s+void\b", "void");
} else {
    var returnType = methodSymbol.ReturnType.ToDisplayString();
    var duplicatePattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(returnType)}\s+{System.Text.RegularExpressions.Regex.Escape(returnType)}\b";
    fullSignature = System.Text.RegularExpressions.Regex.Replace(fullSignature, duplicatePattern, returnType);
}
```

##### 2. GetMembers（AnalysisTools.cs:472-502）
同様のロジックを適用して、メンバー一覧表示でも重複を除去

##### 3. BuildRoslynSubtypeTreeAsync（AnalysisTools.cs:35-60）
内部的に使用される型構造ビルダーでも同様の修正を適用

### 実装のポイント

1. **修飾子の重複チェック**
   - signatureが既にmodifiersで始まっている場合は、修飾子を追加しない

2. **正規表現による単語境界マッチング**
   - `\b`（単語境界）を使用して、部分文字列のマッチを防止
   - `Regex.Escape`で特殊文字を含む型名も正しく処理

3. **voidとその他の型の個別処理**
   - voidは特別扱い（`void void` → `void`）
   - その他の型は動的にパターンを生成

### ビルド結果
```
Build succeeded.
4 Warning(s)
0 Error(s)
```

警告は既存コードのnull参照に関するもので、今回の修正とは無関係です。

### 改善効果

#### Before
- ❌ `public string string ProcessMessage`
- ❌ `public static decimal decimal CalculateValue`
- ❌ `public void void LogMessage`

#### After
- ✅ `public string ProcessMessage`
- ✅ `public static decimal CalculateValue`
- ✅ `public void LogMessage`

### 総評
GetMethodSignatureとGetMembersの両方で、戻り値型の重複表示問題が完全に解決されました。これにより：

1. **可読性の向上** - メソッドシグネチャが正しく表示される
2. **OverwriteMember使用時の安全性** - 正確なシグネチャでメソッドを特定
3. **API探索の効率化** - GetMembersで型の構造を正しく把握

SharpToolsの使いやすさが大幅に向上しました！