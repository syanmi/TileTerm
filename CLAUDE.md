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

## アーキテクチャ（M2時点）

### ペイン分割（`PaneTree.cs` / `PaneManager.cs`）

RLoginのような「1ウィンドウを好きなだけ分割する」レイアウトは、二分木で表現している。

- `LeafNode` = 1枚の実ペイン（`TerminalPaneControl` を保持）
- `SplitNode` = 領域を2つに分けるノード（`Direction: Right/Down`、`First`/`Second`の子、実際の分割線を描画する`Grid`(+`GridSplitter`)を保持）
- `PaneManager` がこの木を操作する唯一の場所: `Split(leaf, direction, profile)` で対象ペインを新しい`SplitNode`に置き換え、`Close(leaf)` で対象を取り除いてその兄弟を親の位置に昇格させる（木の畳み込み）

**ハマった実装バグとその教訓**: `Split`/`Close`共通の「子ノードを差し替える」ヘルパーの中で毎回「古い要素をVisualツリーから外す」処理を呼んでいたところ、`Split`では呼び出し側が対象を*先に*新しいGridへ move 済みだったため、この内部の「外す」処理が「新しい(正しい)親」から誤って外してしまい、該当ペインが描画されない不具合が起きた（分割で3ペイン目を作ると、そのうち1ペインが真っ黒になる形で顕在化）。**WPFの要素は同時に1つの親しか持てないため、「要素を動かす」コードを書くときは、どの時点で・どの親から・何が誰の責任で外すのかを一箇所に集約し、二重に外そうとしないこと。**動作確認（実機でのペイン分割・入力テスト）をしなければ気づけなかった類のバグなので、UIレイアウトを操作するコードは必ずビルド後に実際に操作して確認する。

### プロファイル管理（`ProfileStore.cs` / `ProfileSettingsWindow.cs`）

- 保存先: `%AppData%\TileTerm\profiles.json`（JSON、`System.Text.Json`）
- 初回起動時、このマシンに実在するアプリだけを自動検出してデフォルトプロファイルを作る: Command Prompt(常時) / Windows PowerShell(`System32`にあれば) / Git Bash(`C:\Program Files\Git\bin\bash.exe`があれば、`--login -i`付き) / Claude Code(`PATH`上に`claude`があれば)
- 設定UI(`ProfileSettingsWindow`)からプロファイルの追加・編集・削除が可能。「設定」ボタンはメイン画面上部のツールバーから開く
- **ハマったポイント**: npmでインストールされるCLI等は `.exe` ではなく `.cmd`/`.bat` シムであることが多く、ConPTY(CreateProcess)は`.cmd`/`.bat`を直接起動できない。`ProcessLaunchResolver.cs` で拡張子が`.cmd`/`.bat`なら自動的に `cmd.exe /c <path> <args>` に置き換えることで対応している

### カスタムタイトルバー（M3）とペイン操作の導線

OSデフォルトの装飾（`WindowStyle="None"` + `WindowChrome`）を捨て、VSCodeのようなモダンなアプリに合わせた自前のタイトルバーにしている（`MainWindow.xaml`）。左から: アイコン → 分割→/分割↓/タイルを閉じる/設定...ボタン → (ドラッグ可能な余白+タイトル文字) → 最小化/最大化/閉じるボタン、という並び。

- **分割→/分割↓/タイルを閉じる**は「今アクティブなペイン」に対して働く（`PaneManager.SplitActive`/`CloseActive`）。分割ボタンを押すとその時点の`ProfileStore.Profiles`一覧がコンテキストメニューで出て、選んだプロファイルで新しいペインが開く
- 各`TerminalPaneControl`自身のヘッダーは、プロファイル名の表示 + **更新**(このタイルだけセッションを再起動)+ **×**(このタイルを閉じる)の3つだけに絞った。分割操作はすべてメインウィンドウのタイトルバー側に統一している
- ウィンドウの閉じるボタンを押すと確認ダイアログ（「開いているすべてのタイルを閉じます。よろしいですか？」）が出て、OKで全セッションを`ShutdownAll()`してから終了、キャンセルなら`e.Cancel = true`で閉じるのをやめる
- `WindowChrome`使用時の既知の落とし穴: 最大化すると見えない状態のリサイズ枠がウィンドウの一部として扱われ続け、コンテンツの端が数px切れる。`SystemParameters.WindowResizeBorderThickness` + `WindowNonClientFrameThickness` を足した分だけ最大化時にコンテンツ側へマージンを足す、という定番の回避策を`StateChanged`ハンドラで行っている

## マイルストーン進捗

- ✅ **M1: 単一ターミナルが動くところまで**（2026-09-08 完了）
  - `dotnet new sln` / `dotnet new wpf` でプロジェクト作成
  - ConPTY(Porta.Pty) + XTerm.NET で cmd.exe を1セッション起動し、実際にキー入力・画面描画（日本語ディレクトリ名を含む `dir` コマンド出力等）が正しく動作することをビルド後に実機で確認済み
  - 起動するプロファイルは `ProfileDefinition.Default`（cmd.exe固定）にハードコードされている

- ✅ **M2: コンセプト機能一式**（2026-09-08 完了）
  - ペイン分割UI（右/下split、ネスト分割、close時の木の畳み込み）を実装
  - プロファイル設定UI＋`profiles.json`永続化、初回起動時のデフォルトプロファイル自動検出を実装
  - 実機で以下を確認済み: 右split(Command Prompt + Git Bash、互いに独立してキー入力が届く)、既存ペインの下split(3ペインのネスト表示)、ペインを閉じたときのレイアウト再構成、設定画面でのプロファイル一覧表示・選択・フォーム反映、**Claude Code CLI (`claude`) を実際にペイン内で起動し、TUIの確認画面が正しく描画されることまで確認**（コンセプトで名指しされていた4つの対象アプリ: cmd/PowerShell/Git-bash/claude、すべて動作確認済み）
  - 動作確認はSendKeysではなく`PrintWindow`(DWM合成中でも取得できる)によるキャプチャと、UI Automationの`InvokePattern`によるボタン操作で行った。**このデスクトップ環境では`SetForegroundWindow`や合成`SendKeys`によるウィンドウのフォアグラウンド化が信頼できず**（他のウィンドウにフォーカスを奪われる／`GetWindowRect`で取れる座標へ実際にクリックしても別ウィンドウに当たる等）、UI Automation経由の操作とPrintWindowでのキャプチャの組み合わせが安定して機能した。今後この手のUI自動確認をする際はこの方式を使うこと。

- ✅ **M3: モダンなカスタムタイトルバー化**（2026-09-08 完了）
  - OS標準のタイトルバーを廃止し、`WindowChrome`ベースの自前タイトルバーに変更（アイコン/分割→/分割↓/タイルを閉じる/設定/最小化/最大化/閉じる）
  - 各ペインのヘッダーから分割ボタンを削除し、「更新(再起動)」ボタンを追加。分割操作はタイトルバー側に一本化
  - ウィンドウを閉じる際の確認ダイアログ（OK/キャンセル）を追加
  - 実機で以下を確認済み: タイトルバーからの右split、ペインの更新ボタンによるセッション再起動（再起動前に入力した内容が消え、新しいプロセスの初期画面に戻ることを確認）、タイトルバーのタイルを閉じるボタン（アクティブペインが閉じてレイアウトが再構成される）、閉じる確認ダイアログのキャンセル/OK双方の挙動、最大化（タスクバーとの重なりなし）
  - **ハマったポイント**: `更新`ボタンの動作確認で最初「効いていないように見えた」が、原因はテスト側（UI Automationでのボタン特定）が別ペインの同名ボタンを誤って掴んでいたことだった。同じラベルのボタンが複数ペインに存在する場合、名前だけでなく`BoundingRectangle`の座標も突き合わせて対象を一意に特定すること

## 動作確認用スクリプト

`scripts/build_and_run.bat` をダブルクリック（またはコマンドラインから実行）すると、ビルド〜アプリ起動までを1回で行う。ユーザー自身がClaude Codeを介さずに最新のビルドを動作確認できるようにするためのもの。

- 実行内容: `dotnet build TileTerm.sln -c Debug` → 成功したら `src/TileTerm/bin/Debug/net9.0-windows/TileTerm.exe` を起動 → アプリを閉じるとexit codeを表示して`pause`
- **バッチファイル内のメッセージは意図的に英語(ASCII)にしている**: 日本語(UTF-8)で書いたところ、cmd.exeの既定コードページ(Shift-JIS)との不一致でコマンド自体が文字化けし、`chcp 65001`を先頭に置いても解決しなかったため。今後このファイルを編集する際も日本語は使わないこと。

## 未実装（コンセプト自体には含まれない、品質・UX向上の候補）

コンセプトで挙げられていた機能（画面分割・複数セッション・任意のコンソールアプリをプロファイルで設定可能）はM2で一通り動作するところまで実装済み。以下は今後の改善候補:

- レンダリングのパフォーマンス最適化（同一属性の連続セルをまとめて1回のDrawTextにする、色/フォントのBrush/Typefaceをキャッシュする等。現状は逐セル毎回生成でCPU負荷が高め）
- Bold/Underline/Inverse 以外のテキスト属性（Italic, Dim, Strikethrough, Blink, Overline）の描画
- ウィンドウリサイズ以外のきっかけ（DPI変更等）でのセルメトリクス再計算
- ペインの分割比率・レイアウトの保存/復元（現状は再起動すると単一ペインに戻る）
- プロファイルへのアイコン/色設定、複数ウィンドウ対応、タブ機能
- 最大化時のコンテンツ余白補正がやや粗い（`SystemParameters`頼みのため、環境によっては1px単位の誤差が残る可能性がある）

## 名前の由来・検討経緯

「Terminal」+「Tile」（画面をタイル状に分割してセッションを配置する機能）から命名。
命名検討時には RLogin, Wireshark, MultiTerm (SDL/RWS の用語管理ツールと衝突するため回避), termbox-go 等の既存ソフトウェア名を調査した上で、機能が直感的に伝わりやすいことを優先してこの名前に決定した。
