# 新規Statelessツール実装レポート

実装日時: 2025-06-24
実装者: Claude Code

## 概要

推奨優先度の高い3つのStatelessツールを実装しました。これにより、事前にソリューションをロードすることなく、より多くの機能が利用可能になりました。

## 実装したツール

### 1. AnalyzeComplexity_Stateless

#### 機能
- メソッド、クラス、プロジェクトレベルでのコード複雑度分析
- 循環的複雑度、認知的複雑度、メソッド統計の取得
- リファクタリング推奨事項の提供

#### 特徴
- 既存のComplexityAnalysisServiceをそのまま活用
- FuzzyFqnLookupServiceによる柔軟なシンボル検索
- contextPath基盤の柔軟なスコープ指定

#### パラメータ
```csharp
- contextPath: ファイル、プロジェクト、ソリューションのパス
- scope: "method", "class", "project"
- target: 分析対象の完全修飾名またはプロジェクト名
```

#### 戻り値
```json
{
  "scope": "method|class|project",
  "target": "分析対象名",
  "contextInfo": {
    "contextPath": "使用したコンテキストパス",
    "contextType": "File|Project|Solution"
  },
  "metrics": { /* 複雑度メトリクス */ },
  "recommendations": ["推奨事項1", "推奨事項2", ...]
}
```

### 2. ManageUsings_Stateless

#### 機能
- using文の読み取りと書き込み
- グローバルusing文の処理
- using文の完全置換（追加・削除・並び替え）

#### 特徴
- ファイル固有の処理（CreateForFileAsyncを使用）
- GlobalUsings.csファイルの自動検出と処理
- EditorConfigとの統合（継承）

#### パラメータ
```csharp
- filePath: 対象ファイルの絶対パス
- operation: "read" または "write"
- codeToWrite: "read"時は"None"、"write"時はusing文の完全リスト
```

#### 戻り値（read時）
```json
{
  "file": "ファイルパス",
  "usings": "using文リスト",
  "globalUsings": "グローバルusing文リスト",
  "contextInfo": {
    "contextType": "File",
    "projectName": "プロジェクト名"
  }
}
```

### 3. ManageAttributes_Stateless

#### 機能
- 宣言の属性の読み取りと書き込み
- 属性の完全置換（追加・削除・並び替え）
- FuzzyFqnLookupによる柔軟な対象検索

#### 特徴
- contextPath基盤の柔軟なスコープ指定
- MemberDeclarationSyntaxとStatementSyntaxの両方に対応
- 属性の構文解析とバリデーション

#### パラメータ
```csharp
- contextPath: ファイル、プロジェクト、ソリューションのパス
- operation: "read" または "write"
- codeToWrite: "read"時は"None"、"write"時は属性の完全リスト
- targetDeclaration: 対象宣言の完全修飾名
```

#### 戻り値（read時）
```json
{
  "file": "ファイルパス",
  "line": 行番号,
  "attributes": "属性リスト",
  "contextInfo": {
    "contextPath": "コンテキストパス",
    "contextType": "File|Project|Solution",
    "targetDeclaration": "対象宣言名"
  }
}
```

## 技術的特徴

### 共通実装パターン
```csharp
[McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ToolName_Stateless), ...)]
public static async Task<TResult> ToolName_Stateless(
    StatelessWorkspaceFactory workspaceFactory,
    // Service dependencies...
    ILogger<AnalysisToolsLogCategory> logger,
    // Tool-specific parameters...
    CancellationToken cancellationToken = default)
{
    return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
        // Parameter validation
        ErrorHandlingHelpers.ValidateStringParameter(...);
        
        // Create workspace
        var (workspace, context, contextType) = await workspaceFactory.CreateForContextAsync(...);
        
        try {
            // Tool-specific logic
            // ...
            return result;
        } finally {
            workspace?.Dispose();
        }
    }, logger, nameof(ToolName_Stateless), cancellationToken);
}
```

### リソース管理
- **workspace.Dispose()**の確実な実行
- **try-finally**パターンによるメモリリーク防止
- **StatelessWorkspaceFactory**による効率的なワークスペース作成

### エラーハンドリング
- **ErrorHandlingHelpers**による統一されたエラー処理
- **McpException**による適切なエラー情報の提供
- **パラメータバリデーション**の実装

## テストケース

`TestNewStatelessTools.cs`を作成し、以下のテストケースを実装：

1. **AnalyzeComplexity_Stateless**
   - メソッドレベル分析
   - クラスレベル分析
   - プロジェクトレベル分析

2. **ManageUsings_Stateless**
   - using文の読み取り
   - グローバルusing文の確認

3. **ManageAttributes_Stateless**
   - 属性の読み取り
   - 対象宣言の検索

## パフォーマンス特性

### 利点
- **初回実行が高速**: ソリューションの事前ロード不要
- **メモリ効率的**: 必要な部分のみワークスペース作成
- **柔軟なスコープ**: contextPathによる適応的な処理範囲

### 既存Statelessツールとの一貫性
- **SearchDefinitions_Stateless**と同じパターン
- **AddMember_Stateless**と同じリソース管理
- **OverwriteMember_Stateless**と同じエラーハンドリング

## 使用例

### AnalyzeComplexity_Stateless
```csharp
// メソッドの複雑度分析
var result = await AnalyzeComplexity_Stateless(
    workspaceFactory, complexityService, fuzzyLookupService, logger,
    "SharpTools.Tools.csproj", "method", "SolutionManager.LoadSolutionAsync", 
    cancellationToken);

// クラスの複雑度分析
var result = await AnalyzeComplexity_Stateless(
    workspaceFactory, complexityService, fuzzyLookupService, logger,
    "SharpTools.Tools.csproj", "class", "SolutionManager",
    cancellationToken);
```

### ManageUsings_Stateless
```csharp
// using文の読み取り
var result = await ManageUsings_Stateless(
    workspaceFactory, modificationService, logger,
    "Services/SolutionManager.cs", "read", "None",
    cancellationToken);

// using文の書き込み
var result = await ManageUsings_Stateless(
    workspaceFactory, modificationService, logger,
    "Services/SolutionManager.cs", "write", 
    "using System;\nusing Microsoft.CodeAnalysis;",
    cancellationToken);
```

### ManageAttributes_Stateless
```csharp
// 属性の読み取り
var result = await ManageAttributes_Stateless(
    workspaceFactory, modificationService, fuzzyLookupService, logger,
    "SharpTools.Tools.csproj", "read", "None", "SolutionManager",
    cancellationToken);

// 属性の書き込み
var result = await ManageAttributes_Stateless(
    workspaceFactory, modificationService, fuzzyLookupService, logger,
    "SharpTools.Tools.csproj", "write", 
    "[Obsolete(\"Use new version\")]\n[EditorBrowsable(EditorBrowsableState.Never)]",
    "SolutionManager.OldMethod",
    cancellationToken);
```

## 今後の展開

### 次の実装候補
1. **ReadTypesFromRoslynDocument_Stateless** - 型情報取得の簡易版
2. **MoveMember_Stateless** - 単一ファイル内でのメンバー移動（限定版）

### 改善案
1. **キャッシング**: 同じコンテキストでの連続実行の高速化
2. **バッチ処理**: 複数操作の一括実行
3. **設定ファイル対応**: EditorConfigとの更なる統合

## まとめ

3つのStatelessツールの実装により、SharpToolsのステートレス化が大幅に進みました。これらのツールは：

- **高い実用性**: コード品質向上に直結する機能
- **技術的安定性**: 既存パターンの踏襲による安全な実装  
- **柔軟性**: contextPathによる適応的な処理

次のステップとして、ステートフル版の削除を進めることで、よりシンプルで保守性の高いアーキテクチャを実現できます。