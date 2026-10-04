# VideoLayer

Beat Saberのカスタム曲に動画を表示するBSIPAプラグインです。曲フォルダに置いたMP4動画を、CameraPlusまたはCamera2の三人称カメラの動きに合わせて表示します。背景用の`back.mp4`と前面の演出用の`front.mp4`を、楽曲の再生時間に合わせて再生します。

![Target game](https://img.shields.io/badge/Beat%20Saber-1.44.1-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## 動作環境

- WindowsのPC版Beat Saber **1.44.1**を対象に開発しています。他のバージョンとの互換性は未確認です。
- BSIPA 4.3.4以上、BeatSaberMarkupLanguage 1.12.0以上、SongCore 3.11.0以上。詳細は[manifest.json](manifest.json)に記載しています。
- CameraPlusまたはCamera2の三人称カメラ。

動画は、検出した三人称カメラ1台に表示します。CameraPlusを優先し、見つからなければCamera2などのカメラを探します。複数のカメラに同時に表示する機能はありません。カメラの構成や他のMODとの組み合わせによって、カメラの検出や動画の表示結果が異なる場合があります。

## インストール

1. [Releases](https://github.com/tayo-shirosugi/VideoLayer/releases)から`VideoLayer-vX.X.X.zip`をダウンロードします。
2. ZIP内の`Plugins/VideoLayer.dll`をBeat Saberの`Plugins`フォルダにコピーします。ゲームが起動中の場合は、コピーする前に終了してください。

ゲーム本体や動作に必要な他のMODのDLLは、配布ZIPに含まれていません。「動作環境」に記載したMODは別途インストールしてください。

## 使い方

演出を付けたいカスタム曲のフォルダに、`back.mp4`と`front.mp4`を置きます。背景用と前面用のどちらか一方だけでも使えます。

```text
Beat Saber_Data/CustomLevels/<曲フォルダ>/
  info.dat
  back.mp4           # アバター背後の背景
  front.mp4          # カメラ前面の演出
  videolayer.json    # 任意の曲別設定
```

動画はMP4 / H.264形式を想定しています。再生できる形式は、UnityのVideoPlayerと使用環境によって異なります。動画の音声はミュートされます。

表示サイズは、動画の縦横比を保ちながらカメラの画面に合わせて自動調整します。カメラの縦横比や画角を変えると、動画の表示サイズも変わります。動画とカメラの縦横比が異なる場合は、上下または左右に余白ができます。

楽曲の一時停止、シーク、再生速度の変更に合わせて動画を制御します。曲より短い動画は、再生が終わると非表示になります。動画の再生範囲内まで巻き戻すと、再生を再開します。

HMDには動画を表示せず、検出した三人称カメラだけに表示する設計です。実際の表示は、カメラMODの設定も含めて確認してください。

### 背景の透過

`ChromaKey`は黒背景を透明にする描画モードです。黒だけでなく、圧縮などでわずかに明るくなった黒も透過し、境界を滑らかにします。動画に映る黒い物体も透明になるため、黒色を残したい動画では`GreenKey`または`Opaque`を使ってください。

`GreenKey`は、緑背景（`#00FF00`）の文字PV向けの描画モードです。背景の緑を透過し、文字の縁に残る緑を抑えます。黒い文字も表示できますが、文字や図形の緑色も透過・補正の対象になります。動画の背景色に合わせて、`FrontBlendMode`や`BackBlendMode`を`GreenKey`に変更してください。初期値は黒背景用の`ChromaKey`です。

`PureAdditive`は加算描画を行います。

## 設定

全体設定は`UserData/VideoLayer.json`に保存され、曲が始まるたびに読み込まれます。メインメニューの **MODS → VideoLayer** では、プラグインの有効・無効と背景・前面動画の距離を変更して保存できます。

描画モードや解像度など、メニューにない設定は`UserData/VideoLayer.json`を直接編集して変更します。編集はゲームを終了した状態で行ってください。

| 設定 | 初期値 | 内容 |
| --- | --- | --- |
| `Enabled` | `true` | プラグインの有効・無効 |
| `BackOffsetMeters` | `2.5` | メインカメラの位置を基準に、背景側へ加える距離（m） |
| `FrontDistanceMeters` | `0.2` | カメラから前面動画までの距離（m） |
| `Overscan` | `1.02` | 表示面の拡大率 |
| `SyncThresholdSeconds` | `0.1` | 楽曲と動画の時間のずれをシークで補正するしきい値（秒） |
| `PauseDetectionSeconds` | `0.12` | 楽曲の再生時間が止まったと判定するまでの時間（秒） |
| `FrontBlendMode` / `BackBlendMode` | `ChromaKey` | 描画モード。`ChromaKey`は黒背景透過、`GreenKey`は緑背景透過、`Opaque`は不透明、`PureAdditive`は加算描画 |
| `FrontRenderQueue` / `BackRenderQueue` | `3999` / `2499` | 描画順 |
| `RenderWidth` / `RenderHeight` | `1920` / `1080` | 初期描画解像度 |
| `MaxRenderWidth` / `MaxRenderHeight` | `1920` / `1080` | RenderTextureの解像度の上限。動画デコーダー自体のメモリ使用量は制限しません |
| `CameraOnlyLayer` | `27` | 動画専用のレイヤー。他のMODと共有しないでください |
| `DebugLog` | `false` | 詳細ログとデバッグキー |

動画の表示中は、表示先以外のカメラから`CameraOnlyLayer`を見えなくします。同じレイヤーにある他のMODのオブジェクトも見えなくなるため、他のMODが使っていないレイヤーを指定してください。

曲ごとに設定を変える場合は、曲フォルダの`videolayer.json`に記述します。変更できるのは、`backOffsetMeters`、`frontDistanceMeters`、`frontBlendMode`、`backBlendMode`の4項目だけです。この4項目のうち書かなかったものには、全体設定の値が使われます。

```json
{
  "backOffsetMeters": 3.5,
  "frontDistanceMeters": 0.25,
  "frontBlendMode": "GreenKey",
  "backBlendMode": "Opaque"
}
```

`DebugLog: true`にすると、プレイ中にF8で同梱の4種類の描画モードを切り替えられます。F9では背景動画の表示・非表示を切り替えられます。

## ビルド

ビルドにはWindows、.NET SDK、対象バージョンのBeat Saberと動作に必要なMODが必要です。CIでは.NET SDK 8.0.xを使っています。SDKが見つからない場合、ビルドスクリプトはUnity Hubの標準のインストール先から、Unity Editor付属のMono / Roslynを探します。古い.NET Framework付属の`csc.exe`には対応していません。

```powershell
.\build.ps1 -BeatSaberDir '<Beat Saberのインストールフォルダ>'
```

ゲームのインストール先は、コマンドの引数、環境変数`BEATSABER_DIR`、`.local.beatsaber.path`、Steamの標準のインストール先の順に確認します。`.local.beatsaber.path`は、ゲームのパスを1行で記入するローカル専用のファイルです。Gitでは追跡しません。NuGetパッケージの取得元は[NuGet.Config](NuGet.Config)で指定しています。

ビルドすると、次のファイルが生成されます。

```text
bin/Release/net472/VideoLayer.dll
dist/VideoLayer-v0.1.0.zip
```

ZIPにはプラグインDLLとMITライセンスが含まれます。通常のビルドでは、DLLをゲームのフォルダにコピーしません。ビルドと同時にインストールする場合は、`-Install`を付けて実行します。ゲームが起動中の場合は、先に終了してください。

```powershell
.\build.ps1 -BeatSaberDir '<Beat Saberのインストールフォルダ>' -Install
```

シェーダーを変更した場合は、ライセンス認証済みのUnity 2022.3 Editorで`.\build-shaders.ps1`を実行してください。WindowsのDirect3D 11向けのAssetBundleを生成し、GPUで描画結果を検証します。通常のビルドとCIでは生成済みのバンドルを使うため、Unity Editorをダウンロードしません。

`build.bat`にも同じ引数を渡せます。`.\package.ps1`では、作成済みのDLLからZIPを作り直せます。

## 謝辞

VideoLayerは、CameraPlusとCamera2のカメラ機能を使って動画を表示しています。両MODの開発・メンテナンスに携わる皆様に感謝します。

- [CameraPlus](https://github.com/Snow1226/CameraPlus)：Snow1226氏をはじめとする開発者・貢献者の皆様。
- [Camera2](https://github.com/kinsi55/CS_BeatSaber_Camera2)：kinsi55氏および貢献者の皆様。

## ライセンス

VideoLayerのライセンスは[MIT](LICENSE)です。
