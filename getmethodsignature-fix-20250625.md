# GetMethodSignature重複表示修正レポート

## 実施日: 2025年6月25日

### 問題の詳細
GetMethodSignatureメソッドで、戻り値型が重複して表示される問題が発生していました。
例: `public string string ProcessMessage(string text)`

### 原因分析
1. `CodeAnalysisService.GetFormattedSignatureAsync`メソッドが非同期メソッドとして命名されているが、実際は同期メソッド
2. `GetFormattedSignatureAsync`の実装で、修飾子と戻り値型の組み立て方に問題がある可能性
3. 修飾子とシグネチャを結合する際に重複が発生

### 実装した修正

#### 1. 非同期呼び出しの修正
```csharp
// 誤ったawait使用を削除
var signature = CodeAnalysisService.GetFormattedSignatureAsync(methodSymbol, false);
```

#### 2. 修飾子の重複チェック追加
```csharp
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
```

#### 3. 正規表現による重複除去の強化
```csharp
// Additional fix for duplicate return type issue
if (methodSymbol.ReturnsVoid) {
    fullSignature = System.Text.RegularExpressions.Regex.Replace(fullSignature, @"\bvoid\s+void\b", "void");
} else {
    var returnType = methodSymbol.ReturnType.ToDisplayString();
    var duplicatePattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(returnType)}\s+{System.Text.RegularExpressions.Regex.Escape(returnType)}\b";
    fullSignature = System.Text.RegularExpressions.Regex.Replace(fullSignature, duplicatePattern, returnType);
}
```

### 改善点
1. **単語境界の使用**: `\b`を使用して、部分文字列マッチを防止
2. **正規表現エスケープ**: 特殊文字を含む型名でも正しく動作
3. **修飾子の重複防止**: signatureが既に修飾子を含む場合は追加しない

### ビルド結果
```
Build succeeded.
4 Warning(s)
0 Error(s)
```

警告は既存コードのnull参照に関するもので、今回の修正とは無関係です。

### 次のステップ
1. 実際のプロジェクトでGetMethodSignatureを実行して動作確認
2. 他の重複表示パターンがないか追加テスト
3. `GetFormattedSignatureAsync`メソッド名を`GetFormattedSignature`に変更することを検討