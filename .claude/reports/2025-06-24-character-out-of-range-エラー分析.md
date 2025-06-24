# "character out of range" エラー分析レポート

実行日時: 2025-06-24
分析者: Claude Code

## エラー概要

ユーザーから報告された「Specified argument was out of the range of valid values. (Parameter 'character')」エラーについて分析しました。

## 影響を受けるツール

### ✅ 再現性あり（確実にエラー）
- **SharpTool_ManageAttributes** - 既存メンバーの属性読み取り
- **SharpTool_ViewDefinition** - 既存メンバーの定義表示  
- **SharpTool_ReadTypesFromRoslynDocument** - ファイル構造読み取り

### ✅ 部分的に再現性あり
- 最初の`AttributeTestProperty`は正常動作
- 後から追加した`MultiplyNumbers`でエラー発生
- ファイルの編集履歴に依存する可能性

## 技術的分析

### エラー発生箇所の特定
```csharp
// AnalysisTools.cs
int matchStartPos = node.SpanStart + match.Index;
var matchLinePos = sourceText.Lines.GetLinePosition(matchStartPos);
```

`GetLinePosition`メソッドで文字位置が範囲外になる際にこのエラーが発生します。

### 推測される原因

#### 1. 文字位置計算の問題
- `node.SpanStart + match.Index`が実際のテキスト長を超える
- ノードの位置情報とマッチ位置の不整合
- キャッシュされた位置情報と実際のファイル内容のズレ

#### 2. エンコーディングの問題
- UTF-8マルチバイト文字（日本語コメントなど）の処理
- バイト位置と文字位置の混同
- BOM（Byte Order Mark）の影響

#### 3. 改行コードの問題
- Windows (CRLF) と Unix (LF) の違い
- 改行コードの変換による位置ズレ
- Gitの自動改行変換の影響

#### 4. ファイル同期の問題
- メモリ上のファイル状態とディスク上の不整合
- Roslynのインメモリキャッシュの問題
- ファイル変更通知の遅延

## ステートフル vs ステートレスの違い

### ステートフル版
- ソリューション全体をメモリに保持
- ファイル変更を追跡してキャッシュ更新
- 位置情報の一貫性が保たれやすい

### ステートレス版  
- 毎回新たにワークスペース作成
- ファイルの最新状態を読み込み
- キャッシュなしで位置計算の不整合が起きやすい

## 解決策の提案

### 1. 即時対策（防御的プログラミング）
```csharp
// 範囲チェックを追加
if (matchStartPos >= 0 && matchStartPos < sourceText.Length) {
    var matchLinePos = sourceText.Lines.GetLinePosition(matchStartPos);
    // ...
} else {
    logger.LogWarning("Match position {Position} is out of range for text length {Length}", 
        matchStartPos, sourceText.Length);
    continue;
}
```

### 2. 根本対策（位置計算の修正）
```csharp
// ノードのSpanではなく、SourceTextから直接位置を計算
var nodeText = node.ToFullString();
var nodeStartInFile = node.FullSpan.Start;
var matchInNode = regex.Match(nodeText);
if (matchInNode.Success) {
    var absolutePosition = nodeStartInFile + matchInNode.Index;
    // この位置を使用
}
```

### 3. エンコーディング対策
```csharp
// UTF-8を明示的に指定
var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var sourceText = SourceText.From(fileContent, encoding);
```

### 4. ステートレス版の改善
- ファイル読み込み時の正規化処理
- 位置計算のバリデーション強化
- エラー時の詳細ログ出力

## 今後の対応

### 短期対応
1. 影響を受ける3つのツールに範囲チェックを追加
2. エラー時の詳細ログ出力を実装
3. ファイル読み込み時の文字エンコーディング明示

### 中期対応
1. 位置計算ロジックの見直しと統一
2. テストケースの追加（マルチバイト文字、改行コード）
3. ステートレス版の位置計算精度向上

### 長期対応
1. Roslynの位置情報APIの適切な使用方法の確立
2. ファイル変更検知メカニズムの改善
3. キャッシュ戦略の見直し

## まとめ

このエラーは、ステートレス化に伴う位置情報の不整合が主な原因と考えられます。特に、ファイル編集後の位置計算で問題が発生しやすく、防御的プログラミングと位置計算ロジックの改善が必要です。

ステートレス版の利点（高速起動、メモリ効率）を維持しながら、位置計算の精度を向上させることが今後の課題となります。