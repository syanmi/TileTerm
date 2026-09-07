# TileTerm プロジェクトについて

このワークスペースは、任意のコンソールアプリケーションを画面分割で同時に操作できる、Windows 向け C# デスクトップターミナルツール「TileTerm」の開発プロジェクトである。

## コンセプト・狙い

- コンソールアプリを操作するための Terminal ツール
- [RLogin](https://osdn.net/projects/rlogin/) を参考に、1つのウィンドウを複数のペインに分割し、複数セッション・複数のコンソールアプリを1つのウィンドウ内で同時に操作できるようにする
- C# のデスクトップアプリとして開発する
- ツールの設計として、任意のコンソールアプリであれば設定次第で何でも操作・制御可能なものを目指す
  - 設定で「プロファイル」としてコンソールアプリの .exe パスと起動引数を指定できる
  - ツール上から新規セッションを開始する際は、選択したプロファイルに応じた画面が起動する
  - これにより、ターミナルツール自体は「任意のコンソールアプリ」に対して変更を加えずに使えるようにする
- 対応させたいコンソールアプリの例: コマンドプロンプト(cmd.exe) / PowerShell / Git Bash / Claude Code CLI (claude) など

## 用語の注記

- 会話の初期段階では起動設定を「プロンプト」と呼んでいたが、一般的な用語法では「プロファイル(profile)」と呼ぶのが適切（Windows Terminal の `profiles.json` 等が典型例）。今後のドキュメント・実装では「プロファイル」の語を使う。

## 技術スタック

- C# / .NET 9 (`net9.0-windows`)、SDK は `global.json` で `9.0.300` に固定（マシンに他バージョンのSDKが混在しているため）
- UI フレームワークは **WPF**
- ソリューション構成:
  - `TileTerm.sln`
  - `src/TileTerm/TileTerm.csproj` — WPF アプリ本体（今のところ唯一のプロジェクト。将来ペイン分割やプロファイル管理が増えたらライブラリに切り出すことを検討）

### ターミナル側の実装方式（重要な技術選定）

任意のコンソールアプリ（cmd.exe だけでなく PowerShell や claude のような対話的 TUI アプリも含む）を正しく動かすには、単純な標準入出力リダイレクトではなく **本物の仮想端末(pty)** が必要という判断から、以下の2つの NuGet パッケージを組み合わせている。どちらも作者は同じ (Tom Laird-McConnell / tomlm)。

- **[Porta.Pty](https://github.com/tomlm/Porta.Pty) 1.0.7** — 子プロセスを Windows の ConPTY 経由で起動する pty ライブラリ。`PtyProvider.SpawnAsync(PtyOptions, ct)` → `IPtyConnection`（`ReaderStream`/`WriterStream`/`Resize`/`ProcessExited`/`ExitCode`）というAPI。
  - 注意: このパッケージの **2.0.0 以降は `.NET 10` 必須**になっている。本プロジェクトは .NET 9 のため、意図的に `netstandard2.0` 向けの **1.0.7** に固定している。将来 .NET 10 に上げるならバージョンアップを検討して良い。
- **[XTerm.NET](https://github.com/tomlm/XTerm.NET) 1.2.0** — VT100/ANSI エスケープシーケンスを解釈する「ヘッドレス」な仮想端末エンジン（xterm.js の C# 移植）。UI描画は一切持たず、`Terminal.Buffer` からセル(`BufferCell`: 文字・色・属性)を読み取って自前でレンダリングする方式。
  - こちらも **2.0.0 以降は `.NET 10` 必須**なので、同じ理由で **1.2.0** に固定している。
  - キー入力は `Terminal.GenerateKeyInput(Key, KeyModifiers)` / `GenerateCharInput(char, KeyModifiers)` を使ってエスケープシーケンス文字列を生成し、それを UTF-8 バイト列として `IPtyConnection.WriterStream` に書き込む。この2つのパッケージ以外に自前でVT100パーサやConPTYのP/Invokeを書く必要はない。

パッケージのバージョンを上げる際は、README上の説明だけでなく **NuGetパッケージに同梱された README.md や、リフレクションで実際の公開APIを確認してから**変更すること（メジャーバージョン間でAPIやターゲットフレームワークが変わっているため）。

### レンダリング方式

`TerminalCanvas`（`FrameworkElement` を継承したカスタムコントロール、`src/TileTerm/Terminal/TerminalCanvas.cs`）が `XTerm.Terminal.Buffer` を1セルずつ読み取り、`DrawingContext.DrawText` で等幅フォント(Consolas)のグリッドとして描画している。256色/RGBカラーの変換は `AnsiPalette.cs` に実装。現状は「まず正しく動く」を優先したシンプルな逐セル描画で、パフォーマンス最適化（同一属性の連続セルをまとめて1回のDrawTextにする等）は未実施。

## マイルストーン進捗

- ✅ **M1: 単一ターミナルが動くところまで**（2026-09-08 完了）
  - `dotnet new sln` / `dotnet new wpf` でプロジェクト作成
  - ConPTY(Porta.Pty) + XTerm.NET で cmd.exe を1セッション起動し、実際にキー入力・画面描画（日本語ディレクトリ名を含む `dir` コマンド出力等）が正しく動作することをビルド後に実機で確認済み
  - 起動するプロファイルは `ProfileDefinition.Default`（cmd.exe固定）にハードコードされている

### 未実装（次のマイルストーン候補）

- ペイン分割UI（RLoginのような画面分割・複数セッション同時表示）
- プロファイル設定UI・設定ファイルの永続化（.exe パス・引数をユーザーがGUI上で登録・選択）
- レンダリングのパフォーマンス最適化（同一属性セルのランまとめ、キャッシュ等）
- Bold/Underline/Inverse 以外のテキスト属性（Italic, Dim, Strikethrough, Blink, Overline）の描画
- ウィンドウリサイズ以外のきっかけ（DPI変更等）でのセルメトリクス再計算

## 名前の由来・検討経緯

「Terminal」+「Tile」（画面をタイル状に分割してセッションを配置する機能）から命名。
命名検討時には RLogin, Wireshark, MultiTerm (SDL/RWS の用語管理ツールと衝突するため回避), termbox-go 等の既存ソフトウェア名を調査した上で、機能が直感的に伝わりやすいことを優先してこの名前に決定した。
