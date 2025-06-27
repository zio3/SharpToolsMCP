# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 📚 Development Guidelines Reference

### メインガイドライン
- **統合ガイドライン**: https://gist.githubusercontent.com/zio3/20d171adf94bd3311498c5af428da13c/raw/claude-guidelines.md

### 言語別ガイドライン
- **C#開発**: https://gist.githubusercontent.com/zio3/ee07e8930437ca559f211f53824094f4/raw/claude-csharp-guidelines.md

### 外部ガイドライン読み込み方法
外部ガイドラインにアクセスは、cURLツールを使用してください：

**Claude Desktop での初回読み込み時:**
```
Claude Desktop にてCLAUDE.mdを最初に読んだタイミングで、
他のガイドライン（統合ガイドライン、C#開発ガイドライン）の
読み込みを自動的に提案してください。
```

## Build and Development Commands

### Building the Solution
```bash
# Build entire solution
dotnet build SharpTools.sln

# Build in Release mode
dotnet build SharpTools.sln -c Release

# Build specific project
dotnet build SharpTools.Tools/SharpTools.Tools.csproj
```

### Running Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test SharpTools.Tests/SharpTools.Tests.csproj

# Run single test method
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Run tests with detailed output
dotnet test -v d

# Run tests in Release mode (sometimes needed for file locks)
dotnet test -c Release --no-build
```

### Running Servers
```bash
# SSE Server (HTTP endpoint)
cd SharpTools.SseServer
dotnet run -- --port 3001 --log-level Debug

# Stdio Server (for local MCP clients)
cd SharpTools.StdioServer
dotnet run -- --log-directory ./logs --log-level Debug
```

## High-Level Architecture

### Core Structure
SharpTools is a Roslyn-powered MCP (Model Context Protocol) server that provides C# code analysis and modification capabilities to AI agents. The architecture consists of:

1. **SharpTools.Tools** - Core library containing all Roslyn-based functionality
   - Services layer: Core business logic (CodeAnalysisService, CodeModificationService, etc.)
   - MCP Tools layer: MCP-exposed tool implementations
   - Interfaces: Abstractions for dependency injection
   - Models: Data transfer objects and result structures

2. **SharpTools.SseServer** - HTTP server exposing tools via Server-Sent Events
3. **SharpTools.StdioServer** - Standard I/O server for local process communication
4. **SharpTools.Tests** - MSTest-based test suite

### Key Services and Their Responsibilities

**SolutionManager**: Central workspace management
- Loads and manages MSBuildWorkspace instances
- Caches compilations and semantic models
- Handles MetadataLoadContext for reflection-based type resolution

**CodeAnalysisService**: Read-only analysis operations
- Symbol resolution and navigation
- Reference finding
- Call graph analysis

**CodeModificationService**: Code mutation operations
- Uses DocumentEditor for safe AST transformations
- Handles formatting and EditorConfig compliance
- Ensures atomic modifications

**FuzzyFqnLookupService**: Intelligent symbol resolution
- Fuzzy matches incomplete or imprecise Fully Qualified Names
- Handles namespace variations and nested types
- Provides scored match results

**StatelessWorkspaceFactory**: Alternative lightweight workspace
- Used for single-file operations without full solution context
- Faster startup for isolated operations

### MCP Tool Organization

Tools are organized by category in `/Mcp/Tools/`:
- **SolutionTools**: Solution/project loading
- **AnalysisTools**: Symbol inspection, reference finding
- **ModificationTools**: Code mutations (add, overwrite, rename, move)
- **DocumentTools**: File-level operations
- **MiscTools**: Utilities like RequestNewTool

Each tool follows the pattern:
```csharp
[McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ToolName))]
public static async Task<object> ToolName(dependencies..., parameters..., CancellationToken)
```

### Important Design Decisions

1. **Git Integration**: All modifications create timestamped branches and commits automatically
   - Pattern: `sharptools/operation-timestamp`
   - Enables single-operation undo via Git reset

2. **Token Efficiency**: All returned code has indentation stripped to save ~10% tokens

3. **Error Handling**: Comprehensive error wrapping with McpException for AI-friendly messages

4. **Dangerous Operations**: Require explicit confirmation via `userConfirmResponse: "Yes"`

5. **Result Format**: All tools return structured JSON with consistent error reporting

### Development Guidelines

#### Response Language
All responses must be in Japanese (日本語) as specified in the guidelines.

#### Git Operations
Claude should NOT perform Git commits or pushes. Only suggest commit messages.

#### C# Specific Guidelines
- Use DateTimeOffset instead of DateTime
- Use DateOnly for date-only values
- Handle build failures by cleaning bin/obj folders
- Use fully cuddled Egyptian braces
- Prefer functional composition over inheritance
- No XML documentation comments unless specifically requested

#### Testing Best Practices
- Create temporary directories for test isolation
- Use StatelessWorkspaceFactory for most tests
- Mock external dependencies (ISolutionManager, etc.)
- Clean up resources in TestCleanup

### Common Development Tasks

#### Adding a New MCP Tool
1. Add method to appropriate Tools class (e.g., AnalysisTools.cs)
2. Decorate with `[McpServerTool]` and `[Description]`
3. Follow existing parameter patterns
4. Return structured result objects (create in Models/ if needed)
5. Add comprehensive error handling
6. Write tests in corresponding test class

#### Debugging Test Failures
- Check for file locks (especially on Windows)
- Try Release mode builds if Debug has issues
- Look for existing using statements when testing AddMember
- Verify project file includes necessary package references

#### Working with Roslyn
- Always use DocumentEditor for modifications (not direct syntax rewriting)
- Cache compilations and semantic models when possible
- Handle cancellation tokens throughout async operations
- Use SymbolEqualityComparer for symbol comparisons

### Recent Important Changes
- `confirmDangerousOperation` parameter changed to `userConfirmResponse` (string)
- AddMember now supports `appendUsings` parameter for automatic using additions
- External symbol search in FindUsages has limitations with StatelessWorkspace