# SharpTools ステートレス版の制限事項と推奨ワークフロー

更新日: 2025-06-24

## 📊 ステートレス版の現在の状況

### ✅ 完全動作するツール（読み取り専用）

1. **ViewDefinition_Stateless**
   - 単一ファイルからシンボルの定義を読み取り
   - ファイルパス、プロジェクトパス、ソリューションパスすべてで動作

2. **GetMembers_Stateless**
   - 型のメンバー一覧を取得
   - ローカルな解析で完結

### ❌ 現在動作しないツール（書き込み系）

1. **AddMember_Stateless**
2. **OverwriteMember_Stateless**
3. **RenameSymbol_Stateless**
4. **FindAndReplace_Stateless**

**エラー**: "No solution is currently loaded"

### ⚠️ 修正により動作可能になったツール

1. **FindReferences_Stateless**
2. **ListImplementations_Stateless**

## 🔍 技術的な制限の詳細

### 根本的な問題

ステートレス版のツールは独自のワークスペースを作成しますが、内部で使用している`ICodeModificationService`は`ISolutionManager.CurrentSolution`に依存しています。

```csharp
// CodeModificationService内の問題のコード
public async Task<Solution> AddMemberAsync(...) {
    var solution = GetCurrentSolutionOrThrow(); // ← ここでエラー
    // ...
}
```

### アーキテクチャの不整合

1. **ステートレス版**: `StatelessWorkspaceFactory`で一時的なワークスペースを作成
2. **CodeModificationService**: グローバルな`SolutionManager`の状態を期待
3. **結果**: 二つのワークスペース管理システムが共存できない

## 💡 推奨ワークフロー（短期的解決策）

### 1. 読み取り → 分析 → filesystem書き込みパターン

```yaml
推奨フロー:
  1. 読み取り:
     - SharpTool_ViewDefinition_Stateless でコード確認
     - SharpTool_GetMembers_Stateless で構造把握
  
  2. 分析:
     - LLMがコード変更を計画
     - 必要な修正内容を決定
  
  3. 書き込み:
     - filesystem.read_file で現在の内容を取得
     - filesystem.edit_file で直接編集
     - または filesystem.write_file で新規作成
```

### 2. ハイブリッドアプローチ

```yaml
単純な変更:
  - filesystem ツールで直接編集
  
複雑な変更:
  - SharpTool_LoadSolution でソリューション読み込み（従来版）
  - SharpTool_AddMember 等で変更（従来版）
  - 処理完了後、必要に応じてソリューションをアンロード
```

### 3. 具体的な使用例

#### 例1: 新しいメソッドの追加

```bash
# 1. 現在のクラス構造を確認
SharpTool_GetMembers_Stateless(
  contextPath: "src/MyClass.cs",
  fullyQualifiedTypeName: "MyNamespace.MyClass"
)

# 2. filesystemで直接追加
filesystem.edit_file(
  path: "src/MyClass.cs",
  old_string: "    // End of class",
  new_string: "    public void NewMethod() { }\n    // End of class"
)
```

#### 例2: 参照の確認と更新

```bash
# 1. 参照を確認
SharpTool_FindReferences_Stateless(
  contextPath: "MyProject.csproj",
  fullyQualifiedSymbolName: "MyNamespace.MyClass.OldMethod"
)

# 2. 各ファイルを個別に更新
filesystem.edit_file(
  path: "src/Consumer.cs",
  old_string: "obj.OldMethod()",
  new_string: "obj.NewMethod()"
)
```

## 🚀 長期的な解決策（要開発）

### オプション1: ステートレス版の完全実装

- `ICodeModificationService`のステートレス版を作成
- Solutionを引数として受け取る形に変更
- 完全に独立したワークスペース管理

### オプション2: ワークスペースコンテキストの導入

- 一時的なSolutionManagerコンテキストを作成
- ステートレス版でも内部的にSolutionManagerを使用
- 処理完了後に自動的にクリーンアップ

### オプション3: トランザクショナルな変更API

- 変更をバッチで実行
- Roslynの変更APIを直接使用
- SolutionManager依存を完全に除去

## 📋 まとめ

### 現在の達成事項
- ✅ 高速な読み取り系操作（5-25倍高速化）
- ✅ ファイルベースの直接編集
- ✅ 参照検索と実装一覧（修正により動作）

### 制限事項
- ❌ 複雑なリファクタリング操作
- ❌ 型安全な変更操作
- ❌ コンパイルエラーの即時検出

### 推奨事項
1. **単純な変更**: filesystem ツールを使用
2. **コード読み取り**: ステートレス版を使用
3. **複雑なリファクタリング**: 従来版を使用

この制限は技術的な課題であり、将来的には解決可能です。現時点では、上記のハイブリッドアプローチが最も実用的です。