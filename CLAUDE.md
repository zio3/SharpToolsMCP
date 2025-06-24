# CLAUDE.md

このファイルは、Claude Code (claude.ai/code) でのコード作業時のガイダンスを提供します。

## 📚 開発ガイドライン参照

プロジェクトで使用する言語・技術の詳細ガイドラインについては以下を参照してください。

### 言語別ガイドライン
- **C#開発**: @docs/guidelines/csharp-guide.md  
- **言語参照管理**: @docs/guidelines/language-refs.md

### 共通ガイドライン
- **共通制約**: @docs/guidelines/common.md
- **Desktop固有**: @docs/guidelines/desktop.md
- **Code固有**: @docs/guidelines/code.md

## 重要：ClaudeのバージョンによるMODE区分

### Claude Desktop Mode（claude.ai での操作）
- **ファイル編集は原則禁止**
- 分析、設計、レビュー、指示書作成が主な役割
- 実装はClaude Codeへの指示書として作成
- 詳細: @docs/guidelines/desktop.md

### Claude Code Mode（claude.ai/code での操作）
- ファイル編集、実装、ビルド実行が主な役割
- 具体的なコード実装を行う
- 詳細: @docs/guidelines/code.md

## 🚀 C#活用指針

詳細な活用方法については @docs/guidelines/csharp-guide.md を参照してください。

## プロジェクト利用ガイド

GitへのコミットはClaude Codeが自動で行わず、ユーザーの最終確認を必要とします。

## 🔄 ガイドライン管理システム（時刻ベース更新検知）

### 🎯 基本動作ルール
- **通常時**: ローカルファイル（`docs/guidelines/`）を優先参照
- **更新時**: ユーザーが明示的に「ガイドライン更新」を指示した時のみNotesHub参照
- **自動チェック**: 実行しない（ユーザー指示に基づいてのみ実行）

### 🕒 更新検知システム（時刻ベース）
- **ローカル時刻**: `filesystem:get_file_info` でファイルの `lastModified` を取得
- **リモート時刻**: NotesHubの `updatedAt` フィールドを参照
- **判定ロジック**: `NotesHub.updatedAt > ローカル.lastModified` で更新要否を判定

### 利用可能なガイドライン
- **共通ガイドライン**: 基本制約・技術事項（Git制限、日時処理等）
- **Claude Desktopガイドライン**: ファイル操作判断・指示書作成
- **Claude Codeガイドライン**: 実装・ビルド・テスト・通知
- **言語別ガイドライン**: プロジェクト使用言語の開発指針

### 📋 ガイドライン管理コマンド

#### 📖 ガイドライン確認
```
「ガイドラインを読んで」→ ローカルのガイドライン確認（状況に応じて適切なガイドライン参照）
```

#### 🔍 更新チェックコマンド（チェックのみ、ダウンロードなし）
```
「ガイドラインの更新をチェックして」
→ ローカルとNotesHubの更新時刻を比較 → 結果報告のみ

「古いガイドラインがあるか確認して」
→ 各ガイドラインの更新有無を一覧表示

「言語ガイドラインの更新をチェックして」
→ 言語別ガイドラインの更新確認
```

#### 📥 更新実行コマンド
```
「ガイドラインを更新してください」
→ 全ガイドラインをNotesHubから取得してローカル更新

「古いガイドラインのみ更新して」
→ 更新時刻チェック後、古いもののみダウンロード・更新

「C#ガイドラインを更新してください」
→ 特定言語ガイドラインの個別更新
```

### 🗂️ ガイドラインファイル対応表
| ローカルファイル | NotesHubパス | 概要 |
|------------------|--------------|------|
| @docs/guidelines/common.md | `/Claude開発/共通ガイドライン.md` | 共通制約・技術事項 |
| @docs/guidelines/desktop.md | `/Claude開発/ClaudeDesktopガイドライン.md` | Desktop固有ルール |
| @docs/guidelines/code.md | `/Claude開発/ClaudeCodeガイドライン.md` | Code固有ルール |
| @docs/guidelines/csharp-guide.md | `/Claude開発/言語別ガイドライン/C#開発ガイドライン.md` | C#開発指針 |
| @docs/guidelines/language-refs.md | (ローカル専用) | プロジェクト言語参照管理 |