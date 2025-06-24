# C#開発ガイドライン

このガイドラインは、すべてのC#/.NETプロジェクトで適用される開発指針です。

## 🚀 SharpTools活用指針（Claude Desktop限定）

### 基本原則
**Claude DesktopでのC#ファイル分析では、SharpToolsを最優先で使用する**

- ✅ **分析・読み込み**: SharpTools優先（Claude Desktop）
- ✅ **構造把握**: SharpTools必須（Claude Desktop）
- ❌ **Claude Code**: SharpToolsは使用しない（filesystem使用）
- ❌ **避ける**: Claude DesktopでのC#ファイルのfilesystem:read_file連続実行

### Claude Desktop作業時の手順

#### 1. 初期化（Claude Desktop限定）
```
SharpTool_LoadSolution → [ソリューションパス]
SharpTool_LoadProject → [プロジェクト名]
```

#### 2. 基本分析フロー（Claude Desktop限定）
```
SharpTool_GetMembers → 構造把握（メンバー一覧）
SharpTool_ViewDefinition → 実装確認（重要メソッド）
SharpTool_FindReferences → 参照箇所確認（影響範囲）
```

#### 3. Claude Code作業時の手順
```
標準のファイル読み書き機能を使用
- ファイル内容確認
- 段階的編集
- 新規ファイル作成
```

## 📋 Claude動作モード別ガイドライン

### Claude Desktop（分析・設計）
**役割**: 分析・設計・指示書作成のみ

**推奨操作**:
- ✅ SharpTool_GetMembers（構造把握）
- ✅ SharpTool_ViewDefinition（実装確認）
- ✅ SharpTool_FindReferences（影響分析）
- ✅ SharpTool_ListImplementations（継承関係）
- ✅ SharpTool_AnalyzeComplexity（複雑性分析）

**禁止事項**:
- ❌ ファイル直接編集（指示書のみ作成）
- ❌ SharpTools編集機能の使用

### Claude Code（実装・編集）
**役割**: 実装・編集・ビルド・テスト

**推奨操作**:
- ✅ 標準のファイル読み書き機能を使用
- ✅ 段階的な編集とビルド確認
- ✅ 複数ファイルの確認と調整
- ❌ SharpToolsは利用できない環境

## 🎯 編集判断フロー

```
Claude環境の確認
├─ Claude Desktop？
│   ├─ C#分析 → SharpTools使用
│   └─ 実装 → 指示書作成（編集禁止）
└─ Claude Code？
    ├─ C#ファイル編集 → 標準機能で段階的編集
    ├─ 新規C#ファイル → 標準機能でファイル作成
    └─ その他ファイル → 標準機能で操作
```

## 🔧 コーディング規約

### 日時処理
- **DateTimeOffset推奨**（タイムゾーン情報保持）
- **DateOnly使用**（日付のみの場合）
- **DateTime回避**（既存コードの段階的移行）

```csharp
// ✅ 推奨
public DateTimeOffset CreatedAt { get; set; }
public DateOnly BirthDate { get; set; }

// ❌ 避ける
public DateTime CreatedAt { get; set; }
```

### 非同期処理
- **async/awaitパターン推奨**
- **ConfigureAwait(false)の適切な使用**

```csharp
// ✅ 推奨
public async Task<string> GetDataAsync()
{
    var result = await httpClient.GetStringAsync(url).ConfigureAwait(false);
    return result;
}
```

### null許容参照型
- **.NET Core/.NET 5+では有効化推奨**
- **nullable annotations適切に使用**

```csharp
// ✅ 推奨
public string? OptionalValue { get; set; }
public string RequiredValue { get; set; } = "";
```

## 🛠️ .NETバージョン別対応

### .NET Framework (4.8以下)
- **Entity Framework 6.x系**
- **classic ASP.NET MVC/Web API**
- **System.Web系ライブラリ**

### .NET Core/.NET 5+
- **Entity Framework Core**
- **ASP.NET Core MVC/API**
- **Microsoft.Extensions系ライブラリ**

### 共通考慮事項
- **IDisposableの適切な実装**
- **メモリリーク防止**
- **例外処理の統一**

## 📊 Entity Framework ベストプラクティス

### Code First推奨
```csharp
// ✅ 推奨：明示的な設定
public class MyDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired();
        });
    }
}
```

### マイグレーション管理
- **段階的なスキーマ変更**
- **本番適用前のテスト必須**
- **ロールバック計画の準備**

### パフォーマンス考慮
```csharp
// ✅ 推奨：必要なデータのみ取得
var users = await context.Users
    .Where(u => u.IsActive)
    .Select(u => new { u.Id, u.Name })
    .ToListAsync();

// ❌ 避ける：全データ取得
var users = await context.Users.ToListAsync();
```

## 🏷️ ASP.NET MVC/API ベストプラクティス

### Controller設計
```csharp
// ✅ 推奨：責務の分離
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    
    public UsersController(IUserService userService)
    {
        _userService = userService;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        var users = await _userService.GetUsersAsync();
        return Ok(users);
    }
}
```

### Dependency Injection
- **インターフェース使用推奨**
- **ライフタイム適切に設定**
- **循環参照の回避**

## 🔍 品質保証

### コード分析
```csharp
// Claude Desktop: SharpTool_AnalyzeComplexity活用
// Claude Code: 手動レビューと単体テスト
// 循環的複雑度 10以下を目安
// 認知的複雑度 15以下を目安
```

### テスト戦略
```csharp
// ✅ 推奨：単体テストパターン
[Test]
public async Task GetUserById_ValidId_ReturnsUser()
{
    // Arrange
    var userId = 1;
    var expectedUser = new User { Id = userId, Name = "Test" };
    
    // Act
    var result = await _userService.GetByIdAsync(userId);
    
    // Assert
    Assert.That(result, Is.EqualTo(expectedUser));
}
```

## ⚠️ 重要な注意事項

### やってはいけないこと
- ❌ Claude DesktopでのC#ファイル直接編集
- ❌ Claude CodeでのSharpTools使用（利用できない環境）
- ❌ 影響範囲未確認での大規模変更
- ❌ 適切なテスト無しでの本番デプロイ

### 推奨すること
- ✅ Claude Desktop: 事前分析とSharpTools活用
- ✅ Claude Code: 標準機能での段階的実装とビルド確認
- ✅ 小さな変更での確実な進行
- ✅ 型安全性を活かした確実な実装

## 🚀 パフォーマンス最適化

### メモリ効率
```csharp
// ✅ 推奨：Spanを活用
ReadOnlySpan<char> span = text.AsSpan();

// ✅ 推奨：StringBuilder使用
var sb = new StringBuilder();
foreach (var item in items)
{
    sb.AppendLine(item.ToString());
}
```

### 非同期処理最適化
```csharp
// ✅ 推奨：並列処理
var tasks = urls.Select(async url => await httpClient.GetStringAsync(url));
var results = await Task.WhenAll(tasks);
```

## 📚 参考リソース

### 公式ドキュメント
- [.NET API Browser](https://docs.microsoft.com/en-us/dotnet/api/)
- [C# Programming Guide](https://docs.microsoft.com/en-us/dotnet/csharp/)
- [Entity Framework Documentation](https://docs.microsoft.com/en-us/ef/)

### コーディング標準
- [.NET Runtime Coding Style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)
- [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)

---

**最終更新**: 2025-06-24  
**対象**: すべてのC#/.NETプロジェクト  
**適用範囲**: Claude Desktop（SharpTools）, Claude Code（標準機能）  
**バージョン**: 3.1（Claude Code記述を標準機能に調整）