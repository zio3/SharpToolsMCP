# SharpTools パッケージ更新推奨事項

## MSBuild関連パッケージの更新

現在の脆弱性警告を解消するため、以下のパッケージ更新を推奨します：

### 更新対象パッケージ

1. **Microsoft.CodeAnalysis系**
   - 現在: 4.14.0
   - 推奨: 4.15.0 以上（最新安定版）

2. **NuGet.Protocol**
   - 現在: 6.14.0
   - 推奨: 6.15.0 以上（最新安定版）

### 更新方法

```bash
# .NET CLIを使用した更新
dotnet add package Microsoft.CodeAnalysis.Common --version 4.15.0
dotnet add package Microsoft.CodeAnalysis.CSharp --version 4.15.0
dotnet add package Microsoft.CodeAnalysis.CSharp.Workspaces --version 4.15.0
dotnet add package Microsoft.CodeAnalysis.Workspaces.MSBuild --version 4.15.0
dotnet add package NuGet.Protocol --version 6.15.0
```

### 脆弱性警告の一時的な抑制（非推奨）

更新までの一時的な対処として、プロジェクトファイルに以下を追加可能：

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);NU1903</NoWarn>
</PropertyGroup>
```

ただし、セキュリティの観点から**パッケージ更新を強く推奨**します。