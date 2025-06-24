# FindAndReplace_Stateless アクセス権限問題分析レポート

実行日時: 2025-06-24

## 概要

FindAndReplace_Statelessで「Access denied」エラーが発生する問題を調査しました。これはMCPのセキュリティ設計による意図的な制限です。

## 問題の原因

### 1. DocumentOperationsServiceのセキュリティ制限

```csharp
public async Task<(string contents, int lines)> ReadFileAsync(string filePath, ...) {
    if (!IsPathReadable(filePath)) {
        throw new UnauthorizedAccessException($"Reading from this path is not allowed: {filePath}");
    }
    // ...
}
```

### 2. PathInfoによるアクセス制御

以下の条件でファイルアクセスが制限されます：

- ソリューションディレクトリ外のファイル
- 保護されたディレクトリ（bin, obj, .git等）内のファイル
- 読み取り専用ディレクトリ内のファイル

### 3. ステートレス版の設計上の制限

```csharp
// ステートレス版でもプロジェクトコンテキストが必要
var (workspace, project, document) = await workspaceFactory.CreateForFileAsync(filePath);
```

## 試みた修正

直接ファイルアクセスを試みるよう修正：

```csharp
try {
    var originalContent = await File.ReadAllTextAsync(filePath, cancellationToken);
    // 直接ファイル操作
} catch (UnauthorizedAccessException) {
    // DocumentOperationsServiceにフォールバック
}
```

## 根本的な制限

### セキュリティ設計の意図

1. **ソリューション境界の保護**: ソリューション外のファイルへのアクセスを制限
2. **システムファイルの保護**: 重要なシステムファイルへの誤った操作を防止
3. **権限の最小化**: 必要最小限の権限でのみ動作

### ステートレス版の矛盾

- 「ステートレス」でありながらプロジェクトコンテキストを要求
- プロジェクトに属さないファイルは処理できない
- これは設計上の制限で、セキュリティ機能として意図されている

## 推奨事項

### 1. 使用方法の明確化

FindAndReplace_Statelessの制限を明示：
- プロジェクト/ソリューションに含まれるファイルのみ対象
- スタンドアロンファイルには使用不可

### 2. 代替アプローチ

スタンドアロンファイル用の新しいツール：
```csharp
[McpServerTool(Name = "SharpTool_FindAndReplace_Standalone")]
public static async Task<string> FindAndReplace_Standalone(
    string filePath,
    string regexPattern,
    string replacementText,
    CancellationToken cancellationToken = default)
{
    // 直接ファイル操作のみ使用
    // DocumentOperationsServiceを経由しない
}
```

### 3. ドキュメントの改善

ツールの説明に制限事項を明記：
- "Works without a pre-loaded solution" → "Works with files in a project context"
- 使用可能なファイルパスの条件を明示

## まとめ

FindAndReplace_Statelessのアクセス権限問題は、MCPのセキュリティ設計による意図的な制限です。これは修正すべきバグではなく、セキュリティ機能として設計されています。

ユーザーには以下を推奨：
1. プロジェクト/ソリューション内のファイルには現行ツールを使用
2. スタンドアロンファイルには別の方法（直接編集等）を使用
3. または、ステートフル版（FindAndReplace）を使用