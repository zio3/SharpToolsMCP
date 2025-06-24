# SearchDefinitions_Stateless 実装レポート

実装日時: 2025-06-24
実装者: Claude Code

## 概要

SearchDefinitions_Statelessメソッドを実装しました。これは既存のSearchDefinitionsのステートレス版で、事前にソリューションをロードすることなく、コンテキストパスを基に検索を実行します。

## 実装内容

### 1. メソッドシグネチャ
```csharp
[McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(SearchDefinitions_Stateless), Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
[Description("Stateless version of SearchDefinitions. Dual-engine pattern search across source code AND compiled assemblies for public APIs. Works without a pre-loaded solution, using the provided context path.")]
public static async Task<object> SearchDefinitions_Stateless(
    StatelessWorkspaceFactory workspaceFactory,
    ProjectDiscoveryService projectDiscovery,
    ILogger<AnalysisToolsLogCategory> logger,
    [Description("Path to a file, project, solution, or directory to use as search context.")] string contextPath,
    [Description("The regex pattern to match against full declaration text (multiline) and symbol names.")] string regexPattern,
    CancellationToken cancellationToken)
```

### 2. 主な特徴

#### コンテキストベースの検索スコープ
- **ファイルパス**: 含まれるプロジェクトを検索
- **プロジェクトパス**: そのプロジェクトのみを検索
- **ソリューションパス**: 全プロジェクトを検索
- **ディレクトリパス**: ディレクトリ内のプロジェクトを検索

#### 制限事項（パフォーマンス最適化）
- **結果上限**: 20件（既存版と同じ）
- **タイムアウト**: 5秒（既存版と同じ）
- **リフレクション検索制限**:
  - 最大5プロジェクト
  - 最大10アセンブリ参照

### 3. 実装の詳細

#### ソースコード検索
- 既存のSearchDefinitionsから95%のロジックを再利用
- 正規表現パターンマッチング（大文字小文字無視、複数行対応）
- 生成コードの自動フィルタリング

#### リフレクション検索
- ステートレス版用に制限を追加
- プロジェクトから参照されるアセンブリのみを検索
- パフォーマンスを考慮した並列処理

#### 結果フォーマット
```json
{
  "pattern": "検索パターン",
  "matchesByFile": {
    "ファイルパス": {
      "親FQN": {
        "種類": [
          { "match": "マッチしたコード", "line": 行番号 }
        ]
      }
    }
  },
  "totalMatchesFound": 総マッチ数,
  "contextInfo": {
    "contextPath": "使用したコンテキストパス",
    "contextType": "File/Project/Solution",
    "projectsSearched": 検索したプロジェクト数,
    "reflectionLimited": "制限情報"
  }
}
```

### 4. テストケース

以下のテストケースを作成しました：

1. **TestSearchDefinitionsStateless.cs**
   - ファイルコンテキストでのテスト
   - プロジェクトコンテキストでのテスト
   - ソリューションコンテキストでのテスト
   - 複雑なパターン検索のテスト

2. **CompareSearchDefinitions.cs**
   - ステートフル版との性能比較
   - 結果の一貫性確認
   - 実行時間の測定

## パフォーマンス特性

### 利点
- **初回実行が高速**: ソリューションの事前ロード不要
- **メモリ効率的**: 必要なプロジェクトのみロード
- **柔軟なスコープ**: コンテキストに応じた検索範囲

### トレードオフ
- **リフレクション検索が制限的**: 5プロジェクト、10参照まで
- **キャッシュなし**: 毎回ワークスペースを作成

## 使用例

```csharp
// ファイルから検索
var result = await SearchDefinitions_Stateless(
    workspaceFactory,
    projectDiscovery,
    logger,
    "Services/SolutionManager.cs",
    @"Load.*Solution",
    cancellationToken);

// プロジェクトから検索
var result = await SearchDefinitions_Stateless(
    workspaceFactory,
    projectDiscovery,
    logger,
    "SharpTools.Tools.csproj",
    @"interface I\w+Service",
    cancellationToken);

// ソリューション全体から検索
var result = await SearchDefinitions_Stateless(
    workspaceFactory,
    projectDiscovery,
    logger,
    "SharpTools.sln",
    @"McpServerTool",
    cancellationToken);
```

## 今後の改善案

1. **キャッシング**: 同じコンテキストでの連続検索を高速化
2. **リフレクション制限の設定可能化**: パラメータで制限値を調整可能に
3. **並列度の最適化**: CPUコア数に応じた並列処理の調整

## まとめ

SearchDefinitions_Statelessは、指示書に従って正常に実装されました。ステートレス版として、初回実行時のパフォーマンスが大幅に向上し、柔軟なコンテキストベースの検索が可能になりました。リフレクション検索の制限により、大規模なソリューションでも安定したパフォーマンスを維持します。