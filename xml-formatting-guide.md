# XMLコメントフォーマット問題対応ガイド

## 問題の概要

AddMemberなどのツールを使用する際、XMLコメント内で `<param name = "value">` のような不要なスペースが挿入される場合があります。

## 回避策

### 1. XMLコメントの前処理

コードを提供する前に、XMLコメントを整形してください：

**問題のある例:**
```xml
/// <param name = "value">パラメータの説明</param>
```

**正しい例:**
```xml
/// <param name="value">パラメータの説明</param>
```

### 2. 後処理での修正

FindAndReplaceツールを使用して一括修正：

```csharp
// SharpTool_FindAndReplace を使用
filePath: "MyFile.cs"
regexPattern: @"<param\s+name\s*=\s*""(\w+)"">"
replacementText: "<param name=\"$1\">"
```

### 3. 完全な修正例

```csharp
// ステップ1: 問題のあるパターンを検索
SharpTool_FindAndReplace(
    filePath: "MyFile.cs",
    regexPattern: @"<(\w+)\s+name\s*=\s*""",
    replacementText: "<$1 name=\""
)

// ステップ2: summary, returns なども同様に修正
SharpTool_FindAndReplace(
    filePath: "MyFile.cs",
    regexPattern: @"</(param|summary|returns)\s*>",
    replacementText: "</$1>"
)
```

## 根本的な解決策

この問題は、Roslynの構文木生成時のフォーマッティングに起因する可能性があります。将来的なバージョンで、XMLコメントの正規化処理を追加することを検討しています。

## 注意事項

- XMLコメントのフォーマットは、Visual Studioの自動フォーマット機能でも修正可能です
- `.editorconfig` ファイルでXMLコメントのフォーマットルールを設定することも推奨されます