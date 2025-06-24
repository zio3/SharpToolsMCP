# AddMember_Stateless NamespaceDeclarationSyntaxエラー修正レポート

実行日時: 2025-06-24

## 概要

AddMember_Statelessツールで「Unsupported member type: NamespaceDeclarationSyntax」エラーが発生していた問題を修正しました。

## 問題の原因

### エラー発生箇所
```csharp
// ModificationTools.cs:1265行目付近
var addedMemberNode = updatedRoot.DescendantNodes()
    .OfType<MemberDeclarationSyntax>()
    .FirstOrDefault(m => GetMemberName(m) == memberName && m.IsKind(memberSyntax.Kind()));
```

### 根本原因
1. `DescendantNodes()`は構文木の全ての子孫ノードを返す
2. `NamespaceDeclarationSyntax`も`MemberDeclarationSyntax`を継承している
3. `GetMemberName`メソッドが`NamespaceDeclarationSyntax`をサポートしていなかった
4. 結果として「Unsupported member type」例外が発生

## 実装した修正

### 1. GetMemberNameメソッドの拡張
```csharp
private static string GetMemberName(MemberDeclarationSyntax memberSyntax) {
    return memberSyntax switch {
        // ... 既存のケース ...
        NamespaceDeclarationSyntax ns => ns.Name.ToString(), // 追加
        FileScopedNamespaceDeclarationSyntax fsns => fsns.Name.ToString(), // 追加
        _ => throw new NotSupportedException($"Unsupported member type: {memberSyntax.GetType().Name}")
    };
}
```

### 2. 名前空間宣言の除外
```csharp
var addedMemberNode = updatedRoot.DescendantNodes()
    .OfType<MemberDeclarationSyntax>()
    .Where(m => m is not NamespaceDeclarationSyntax && m is not FileScopedNamespaceDeclarationSyntax)
    .FirstOrDefault(m => GetMemberName(m) == memberName && m.IsKind(memberSyntax.Kind()));
```

## 技術的な詳細

### Roslyn構文階層
- `MemberDeclarationSyntax`は基底クラス
- `NamespaceDeclarationSyntax`もこれを継承
- しかし、名前空間はクラスのメンバーではない
- 検索時に明示的に除外する必要がある

### テスト結果
```csharp
// テストプログラムで確認
SyntaxFactory.ParseMemberDeclaration("namespace Test { }") → null
SyntaxFactory.ParseMemberDeclaration("public bool IsEmpty => true;") → PropertyDeclarationSyntax
```

## 影響範囲

- **修正ファイル**: ModificationTools.cs
- **影響ツール**: AddMember_Stateless
- **副作用**: なし（他のツールへの影響なし）

## ビルド結果

```
Build succeeded.
    4 Warning(s) (既存の警告のみ)
    0 Error(s)
```

## まとめ

AddMember_Statelessでメンバー追加後の確認処理において、名前空間宣言が誤って処理対象となっていた問題を修正しました。これにより、ステートレス版のメンバー追加機能が正常に動作するようになります。