# SharpTools テストプロジェクト

このプロジェクトは、SharpToolsのOverwriteMemberおよびGetMethodSignatureで発見されたバグをテストするためのMSTestプロジェクトです。

## 発見されたバグ

### 🚨 優先度: Critical
1. **GetMethodSignatureでのオーバーロード識別不良** - パラメータ指定で常に最後のメソッドが返される
2. **OverwriteMemberでのオーバーロード誤処理** - 間違ったメソッドを更新してコンパイルエラーを引き起こす
3. **コード構造破損** - 文法的に無効なコード（`public /// <summary>...`）を生成

### 🔴 優先度: High  
4. **アクセス修飾子の自動削除** - 元のアクセス修飾子が失われる
5. **シンボル識別不良** - 簡単なメソッド名での識別が機能しない

## テストクラス

### `OverwriteMemberBugTests.cs`
- OverwriteMemberの各種バグをテスト
- オーバーロード処理、コード構造、アクセス修飾子のテスト

### `GetMethodSignatureBugTests.cs`  
- GetMethodSignatureのオーバーロード識別をテスト
- 各パラメータタイプで正確なメソッドが返されるかテスト

## テスト実行方法

### Visual Studio
1. Test Explorer を開く
2. すべてのテストを実行

### コマンドライン
```bash
cd SharpTools.Tests
dotnet test
```

### 特定のテストクラスのみ実行
```bash
dotnet test --filter "FullyQualifiedName~GetMethodSignatureBugTests"
dotnet test --filter "FullyQualifiedName~OverwriteMemberBugTests"
```

## 期待されるテスト結果

**修正前（現在のバグ状態）:**
- GetMethodSignatureBugTests: 一部または全部失敗
- OverwriteMemberBugTests: 一部失敗（特にオーバーロードとコード構造テスト）

**修正後（期待される状態）:**
- 全テストが成功

## テストデータ

`TestData/` フォルダに以下のテスト用クラスが含まれています：
- `OverloadTestClass.cs` - オーバーロードメソッドのテスト用
- `AccessModifierTestClass.cs` - アクセス修飾子のテスト用

## 注意事項

- テストは一時ディレクトリでファイルを作成・変更します
- 各テスト後に自動的にクリーンアップされます
- テストデータファイルは実行時に動的に作成されます
