# SharpTools OverwriteMember バグ修正レポート

## 実施日: 2025年6月25日

## 修正内容

### 🚨 修正1: オーバーロードメソッドの誤処理（最重要）

#### 問題
オーバーロードされたメソッドで間違ったメソッドを更新していた。例：
- `Process(int)`を指定したのに`Process(string)`が更新される
- 結果的に同じシグネチャのメソッドが複数できてコンパイルエラー

#### 解決策
`IsSymbolMatch`メソッドを拡張し、パラメータ型を含むメソッドシグネチャでの正確なマッチングを実装：

```csharp
// 新しいメソッドシグネチャマッチング
if (symbol is IMethodSymbol methodSymbol && fullyQualifiedName.Contains("(")) {
    // Try to match with parameter types
    var methodSignature = BuildMethodSignature(methodSymbol);
    if (methodSignature == fullyQualifiedName)
        return true;
    
    // Also try with fully qualified parameter types
    var fullMethodSignature = BuildMethodSignature(methodSymbol, useFullyQualifiedTypes: true);
    if (fullMethodSignature == fullyQualifiedName)
        return true;
}
```

**サポートされる識別子形式**：
- `Process` - シンプルな名前（オーバーロードがない場合）
- `Process(int)` - パラメータ型指定
- `Process(System.Int32)` - 完全修飾パラメータ型
- `WebApplication3.Tests.OverloadTestClass.Process(int)` - 完全修飾名

### ✅ 修正2: アクセス修飾子の自動継承

#### 問題
新しいコードでアクセス修飾子を含めない場合、元のアクセスレベルが失われていた。

#### 解決策
`ApplyAccessModifiersIfMissing`メソッドを実装し、元のメソッドから自動的にアクセス修飾子を継承：

```csharp
// Apply access modifiers from old node if not present in new node
if (oldNode is MemberDeclarationSyntax oldMember && newNode is MemberDeclarationSyntax newMember) {
    newNode = ApplyAccessModifiersIfMissing(oldMember, newMember);
}
```

**継承される修飾子**：
- アクセス修飾子: `public`, `private`, `protected`, `internal`
- その他の修飾子: `static`, `virtual`, `override`, `abstract`, `sealed`, `async`, `readonly`, `partial`, `extern`

### ✅ 修正3: パーサーの順序修正（前回実装済み）

`ParseMemberDeclaration`を先に使用することで、アクセス修飾子付きメソッドが正しく解析されるようになった。

## テストプロジェクトの作成

`/mnt/c/Users/info/source/repos/Experimental2025/WebApplication3/` に以下のテストファイルを作成：

1. **OverwriteTestClass.cs** - 基本的なメソッドテスト
2. **OverloadTestClass.cs** - オーバーロードメソッドテスト
3. **OverwriteTestClass_Enhanced.cs** - 20種類の異なるメソッドシナリオ
4. **OverloadTestClass_Enhanced.cs** - 10種類の高度なオーバーロードシナリオ

## 期待される改善

### ✅ オーバーロード処理
```csharp
// 修正前: Process(int)を指定してもProcess(string)が更新される
// 修正後: 正確に指定したオーバーロードのみが更新される
SharpTool_OverwriteMember
- fullyQualifiedMemberName: "Process(int)"  // 正確にintバージョンを指定
- newMemberCode: "public string Process(int input) { return $\"Updated: {input}\"; }"
```

### ✅ アクセス修飾子の処理
```csharp
// 修正前: publicを含めるとエラー、含めないとinternalになる
// 修正後: どちらでも正しく動作し、元のアクセスレベルを保持

// パターン1: アクセス修飾子なし（自動継承）
string ProcessMessage(string message) { return message; }
// → 元がpublicならpublicが自動付与

// パターン2: アクセス修飾子あり（そのまま使用）
public string ProcessMessage(string message) { return message; }
// → エラーなく正常に処理
```

### ✅ 識別子の統一
- GetMethodSignatureとOverwriteMemberで同じ識別子形式が使用可能
- エラーメッセージに使用可能な形式のガイダンスを追加予定

## ビルド結果
```
Build succeeded.
4 Warning(s)
0 Error(s)
```

## 残作業
- フォーマット保持の改善
- 識別子仕様の完全統一（GetMethodSignatureとの互換性）
- エラーメッセージの改善

## 総評
最も重要なオーバーロード誤処理バグが修正され、アクセス修飾子の自動継承も実装されました。SharpToolsのOverwriteMemberは、実用的なレベルに大幅に改善されました。