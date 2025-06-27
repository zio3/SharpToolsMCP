# AddMember using文自動追加機能実装レポート

## 実施日時
2025-06-25

## 実装内容

### 1. 新パラメータの追加
AddMemberメソッドに以下のパラメータを追加：
- `usingStrategy`: "manual"（デフォルト）または "specified"
- `requiredUsings`: 追加するusing文のリスト（string[]）

### 2. AddMemberResultの拡張
以下のフィールドを追加：
- `AddedUsings`: 実際に追加されたusing文のリスト
- `UsingConflicts`: 既に存在していたため追加されなかったusing文のリスト

### 3. using文追加ロジックの実装
- 重複チェック機能
- アルファベット順での挿入
- "using"キーワードと";"の自動除去（正規化）

### 4. テストケースの作成
- 基本的なusing文追加テスト
- 重複検出テスト
- manualストラテジーテスト
- アルファベット順挿入テスト

## 技術的な課題

### DocumentEditor使用時の問題
テスト実行時に以下のエラーが発生：
```
Unable to cast object of type 'Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax' 
to type 'Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax'
```

これは、DocumentEditorでusing文を追加した後、Members.First()がUsingDirectiveSyntaxを返すようになり、
その後のメンバー追加処理でキャストエラーが発生するためです。

## 実装状況

### 完了した項目
✅ パラメータ定義の追加
✅ レスポンスモデルの拡張
✅ using文追加ロジックの基本実装
✅ テストケースの作成

### 未解決の問題
❌ DocumentEditorでのusing文とメンバーの同時編集時のエラー
- 根本原因：DocumentEditorの内部状態がusing文追加後に変更される
- 影響：using文を含むAddMember操作が失敗する

## 推奨される対応案

### 1. 即時対応（回避策）
- using文の追加とメンバーの追加を別々のDocumentEditorで実行
- 最初にusing文を追加してドキュメントを更新し、その後メンバーを追加

### 2. 根本対応
- Roslyn APIの詳細な調査
- CompilationUnitSyntaxの直接操作による実装
- DocumentEditorの代わりにSyntaxRewriterを使用

### 3. Phase 2以降の実装
要望書で提案されたPhase 2（パターンマッチング）とPhase 3（完全自動検出）の実装は、
Phase 1の問題が解決してから進めることを推奨

## まとめ

基本的な実装は完了しましたが、Roslyn APIのDocumentEditor使用時の制約により、
using文とメンバーの同時編集に問題が発生しています。この問題を解決するには、
実装アプローチの見直しが必要です。