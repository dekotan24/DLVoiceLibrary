# <img width="32" height="32" alt="app" src="https://github.com/user-attachments/assets/f49d3098-bee0-40a6-b7cb-3c8f96162051" /> DLVoiceLibrary

音声作品(ボイスドラマ・ASMR等)に特化したライブラリ管理+ミュージックプレーヤー。
[DLGameManager](https://github.com/dekotan24/DLGameManager) の音声作品版で、exe起動の代わりにアプリ内再生機能を中核に据えています。

<img width="1266" height="813" alt="2026-07-06_01h18_53" src="https://github.com/user-attachments/assets/ab4e8847-b5ea-4064-983f-17cfadfac2f9" />

## 主な機能

### ライブラリ管理
- 作品フォルダの登録(個別追加 / 親フォルダ一括追加 / ドラッグ&ドロップ)
- **フォルダ階層をそのまま保持したトラックツリー表示**(SEあり/なし等のバリアントフォルダも実構造どおり表示)
- 複数の監視フォルダ: アプリ起動時・実行中に追加された作品フォルダを自動検出して登録
- DLsite・FANZA同人メタデータ自動取得(タイトル / サークル / 声優 / ジャンル / 販売日 / サムネイル)
  - DLsiteはAI生成作品(aix)等の新レイアウト作品にも対応(取得は[DLsiteInfoGetter](https://github.com/dekotan24/DLsiteInfoGetter)に統合)
  - FANZA同人はフォルダ名の `d_123456` 形式のIDから自動判別
- サムネイルのホバー拡大表示
- 作品の一覧からの削除(ファイルは消さない。監視による自動再登録も抑止)
- プロパティ編集(タイトル / サークル / 作品ID / 声優 / ユーザータグ / メモ)

### 検索
- テキスト検索(タイトル / サークル / 声優 / タグ / RJ番号、`Ctrl+F`)
- 詳細検索: タグ複数選択(AND) / 声優複数選択(AND) / サークル / 販売日From-To
- **作品単位のお気に入り**(★)と「お気に入りのみ」絞り込み
- **作品詳細のサークル名・声優名・タグをクリックしてそのまま絞り込み**(一覧カードのサークル名からも可)

### プレーヤー
- MP3 / WAV / FLAC / OGG / M4A 対応(LibVLC)
- シーク / リピート(オフ・1曲・全曲) / シャッフル / 再生速度変更(0.75x〜2.0x)
- 作品をまたぐプレイリスト
- レジューム(トラック単位+アプリ全体で前回の続きから)
- スリープタイマー(時間指定 / 現在のトラック終了時)
- トラックのお気に入り、再生履歴
- **音声出力デバイスの切り替え**(仮想オーディオデバイス等を指定可能)

### Webメディアサーバ
- LAN内の他のPC・スマホ・タブレットのブラウザからライブラリを閲覧・再生
- レスポンシブUI(PC: サイドバー+グリッド / モバイル: タブバー+全画面プレーヤー)
- フォルダ階層ツリー・プレイリスト・お気に入り・最近再生に対応
- Media Session API対応(スマホのロック画面・Bluetooth機器から再生コントロール)
- ブラウザでの再生もアプリ側の再生履歴・再生回数に反映
- ユーザー名+パスワード認証(任意)、シーク対応ストリーミング(Range)
- 設定画面から開始/停止・ポート変更・自動開始を設定

### その他
- 内蔵ブラウザ(WebView2)でDLsiteの作品ページを直接閲覧
- ダークテーマ(Catppuccin Mocha)

## 動作環境

- Windows 10 / 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (内蔵ブラウザ使用時。Windows 11と大半のWindows 10には標準搭載)

## インストール

[Releases](../../releases) から zip をダウンロードして展開し、`DLVoiceLibrary.exe` を実行してください。

データは `%LOCALAPPDATA%\DLVoiceLibrary\` に保存されます(SQLite DB / サムネイルキャッシュ / ログ)。

## ビルド

```
dotnet build
dotnet test
```

Visual Studio 2022以降、または .NET 8 SDK が必要です。

> **Note**: メタデータ取得に [DLsiteInfoGetter](https://github.com/dekotan24/DLsiteInfoGetter) を使用しています。
> リポジトリを本リポジトリと同じ親フォルダにクローンしてください(相対パスでProjectReferenceしています)。

## 使用ライブラリ

- [DLsiteInfoGetter](https://github.com/dekotan24/DLsiteInfoGetter) — DLsite/FANZA同人の作品情報取得
- [LibVLCSharp](https://github.com/videolan/libvlcsharp) — 音声再生エンジン
- [TagLibSharp](https://github.com/mono/taglib-sharp) — 音声ファイルのタグ読み取り
- [AngleSharp](https://anglesharp.github.io/) — HTMLパース
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVMフレームワーク
- [VirtualizingWrapPanel](https://github.com/sbaeumlisberger/VirtualizingWrapPanel) — カードグリッドの仮想化
- [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) — 内蔵ブラウザ

## License

[MIT](LICENSE)
