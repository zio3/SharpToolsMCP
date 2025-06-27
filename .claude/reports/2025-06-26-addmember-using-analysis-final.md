# AddMember using文分析機能 - 最終実装

## 実施日時
2025-06-26

## 最終設計方針

### 🎯 コンセプト
**「実際の修正はしない、分析とレポートのみ」**

- LLMが必要なusing文を指定
- SharpToolsは既存using文との差分を分析
- 結果をレポートして、実際の追加はLLMが別途実行

### 🔧 実装内容

#### 1. パラメータ仕様
```javascript
{
  // 既存パラメータ
  filePath: string,
  memberCode: string,
  className?: string,
  insertPosition?: string,
  fileNameHint?: string,
  
  // 新規パラメータ
  usingStrategy?: "manual" | "analyze",  // デフォルト: "manual"
  requiredUsings?: string[]              // 分析対象のusing文リスト
}
```

#### 2. レスポンス仕様
```javascript
{
  // 既存フィールド
  success: boolean,
  targetClass: string,
  filePath: string,
  addedMembers: AddedMember[],
  statistics: MemberStatistics,
  insertPosition: string,
  message: string,
  compilationStatus?: DetailedCompilationStatus,
  
  // 新規フィールド
  addedUsings: string[],      // 追加が必要なusing文
  usingConflicts: string[]    // 既に存在するusing文
}
```

## 📝 使用例

### LLM側での活用フロー

```javascript
// Step 1: LLMがメンバーコードとusing文を準備
const memberCode = `
public int CalculateSum(IEnumerable<int> numbers) {
    return numbers.Sum();
}`;

const requiredUsings = ["System.Linq", "System.Collections.Generic"];

// Step 2: AddMemberを実行（分析モード）
const result = await AddMember({
  filePath: "MyClass.cs",
  memberCode: memberCode,
  className: "MyClass",
  usingStrategy: "analyze",
  requiredUsings: requiredUsings
});

// Step 3: 結果を確認
if (result.addedUsings.length > 0) {
  // LLMが必要なusing文を追加
  for (const usingNamespace of result.addedUsings) {
    await filesystem.edit_file({
      path: "MyClass.cs",
      old_string: "using System;",
      new_string: `using System;\nusing ${usingNamespace};`
    });
  }
}

// Step 4: 既に存在するusing文の情報も活用
if (result.usingConflicts.length > 0) {
  console.log(`既に存在: ${result.usingConflicts.join(", ")}`);
}
```

## 🛡️ 安全性の担保

### 責任分界点の明確化

1. **SharpToolsの責任範囲**
   - using文の存在チェック
   - 必要なusing文のリストアップ
   - 結果のレポート

2. **LLMの責任範囲**
   - 必要なusing文の判断
   - 実際のファイル編集
   - 編集位置の決定

### エラーリスクの排除

- DocumentEditorでの複雑な操作を排除
- ファイル変更は一切行わない
- 単純な文字列比較のみ実行

## 📊 メリット

1. **実装の簡潔性**
   - 複雑なRoslyn API操作を回避
   - テストが容易
   - バグリスクの最小化

2. **柔軟性の確保**
   - LLMが編集方法を自由に選択可能
   - 特殊なケース（global using等）にも対応可能
   - 将来の拡張が容易

3. **責任の明確化**
   - エラー時の原因特定が容易
   - LLM側で完全なコントロールが可能
   - ユーザーへの説明が明確

## 🚀 今後の展開

### Phase 2以降は必要に応じて

1. **パターンマッチング**（オプション）
   - よく使われるパターンの自動検出
   - ただし、実際の追加はLLM側

2. **より高度な分析**（オプション）
   - 型解決による必要using文の推定
   - ただし、最終判断はLLM側

## まとめ

この実装により、AddMemberは「using文の分析ヘルパー」として機能し、実際の編集責任はLLM側に委ねることで、安全かつ実用的な機能を提供します。複雑な自動化を避けることで、予期しないエラーを防ぎ、LLMの判断を尊重する設計となっています。