# FindAndReplace ステートフル版とステートレス版の違い

実行日時: 2025-06-24

## 概要

ユーザーの質問「ステートフル版だと大丈夫だったのに、なぜステートレス版ではアクセスエラーが発生するのか」を調査しました。

## 主な違い

### 1. エラーハンドリングの違い

**ステートフル版（FindAndReplace）**:
```csharp
// ファイルごとにチェック
var pathInfo = documentOperations.GetPathInfo(file.FullName);
if (!pathInfo.IsWritable) {
    logger.LogWarning("Skipping file due to restrictions: {FilePath}, Reason: {Reason}",
        file.FullName, pathInfo.WriteRestrictionReason ?? "Outside solution directory");
    continue; // スキップして次のファイルへ
}
```

**ステートレス版（FindAndReplace_Stateless）**:
```csharp
// 直接ReadFileAsyncを呼び出し
var (originalContent, _) = await documentOperations.ReadFileAsync(filePath, false, cancellationToken);
// IsPathReadableがfalseなら例外が発生
```

### 2. 処理対象の違い

- **ステートフル版**: 複数ファイルを処理、制限のあるファイルはスキップ
- **ステートレス版**: 単一ファイルを処理、制限があれば即エラー

### 3. 設計思想の違い

- **ステートフル版**: グレースフルな処理（警告を出してスキップ）
- **ステートレス版**: フェイルファスト（即座にエラー）

## 実装した修正

ステートレス版にも事前チェックを追加：

```csharp
// Check path accessibility (similar to stateful version)
var pathInfo = documentOperations.GetPathInfo(filePath);
if (!pathInfo.IsWritable) {
    logger.LogWarning("File is not writable: {FilePath}, Reason: {Reason}",
        filePath, pathInfo.WriteRestrictionReason ?? "Unknown");
    // より分かりやすいエラーメッセージ
    throw new McpException($"Cannot modify file '{filePath}': {pathInfo.WriteRestrictionReason ?? "File is outside solution directory or in a protected location"}");
}
```

## 改善点

1. **明確なエラーメッセージ**: 「Access denied」ではなく、具体的な理由を表示
2. **事前チェック**: ReadFileAsyncで例外が発生する前に検証
3. **ログ出力**: 問題の原因を追跡しやすく

## まとめ

ステートフル版とステートレス版の違いは：

1. **ステートフル版**: 制限のあるファイルを警告付きでスキップ（処理を継続）
2. **ステートレス版**: 制限のあるファイルでエラー（処理を中断）

これは設計上の違いであり、それぞれの使用シナリオに適した動作です。修正により、ステートレス版でもより分かりやすいエラーメッセージが表示されるようになりました。