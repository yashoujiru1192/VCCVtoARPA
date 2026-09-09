# VCCVtoARPA v0.1.1 — OpenUtau v0.1.569.0対応版

## 対応と導入

この版はOpenUtau v0.1.569.0向けです。v0.1.568以前とのDLL互換性は保証しません。
EN ARPA LITEやDeltaCVVCtoARPAとは別のフォネマイザーです。

1. OpenUtauを終了します。
2. 現在使用中のPluginsフォルダーのVccvToArpaPhonemizer.dllを、このZIPの同名DLLで上書きします。
3. OpenUtauを起動し、EN VCCV2ARPAを選択します。説明名はVCCVtoARPA Phonemizer v0.1.1です。
4. Cz式VCCVのUST/USTXとARPAsing音源で変換・再生を確認してください。

既存のvccv-to-arpa.yamlは保持してください。新規導入時は内蔵テンプレートから生成されます。
OpenUtau.Core.dllなどの本体DLLをPluginsへ追加しないでください。

## 変更点

- v0.1.569のnullable toneShift APIへ対応。
- ノートのtoneShift / voiceColor / alternate未指定時にトラック既定値を参照。
- v0.1.569公式配布のOpenUtau.Coreを参照して再コンパイル。
- プロジェクトの対象フレームワークを.NET 10へ更新。
- VCCV記号対応表、識別子、独立設定ファイル名は維持。

## 検証範囲

公式Linux x64 v0.1.569-betaの実DLLでコンパイル・外部読み込み・回帰テストを実施。
合成OTOを使った変換試験で -hE / E l / lO / O - の変換と、未設定属性の処理が成功。
既存回帰試験も合格。Windows GUIでの起動、実音源の聴感は未確認。
全OpenUtauバージョンへの対応を保証するものではありません。

## ソースからビルド

.NET 10 SDKを使用し、v0.1.569のOpenUtau.Core.dllとSerilog.dllをsrc/libへ置いて、
`dotnet build src/VccvToArpaPhonemizer.csproj -c Release`を実行してください。
参照DLLは配布ZIPには含めていません。

公式リリース: https://github.com/openutau/OpenUtau/releases/tag/0.1.569-beta

---
以下は旧版の機能説明です。旧版の対応環境・検証記載より上記を優先してください。

# VCCVtoARPA Phonemizer v0.1.0

OpenUtauでCz式英語VCCVのUST/USTX歌詞を読み、ARPAsing音源用のエイリアスへ
変換する専用フォネマイザーです。

このフォネマイザーは **EN ARPA LITEとは別製品** です。DLL、OpenUtau上の表示名、
設定ファイル、バージョン履歴を共有しません。

| 項目 | VCCVtoARPA |
|---|---|
| DLL | `VccvToArpaPhonemizer.dll` |
| OpenUtau上の識別子 | `EN VCCV2ARPA` |
| 設定ファイル | `vccv-to-arpa.yaml` |
| 入力 | Cz式英語VCCVの歌詞エイリアス |
| 出力先 | ARPAsing音源のエイリアス |

## インストール

1. OpenUtauを終了します。
2. `VccvToArpaPhonemizer.dll`をOpenUtauへドラッグ＆ドロップします。
3. OpenUtauを再起動します。
4. ARPAsing音源を選び、トラックのフォネマイザーを`EN VCCV2ARPA`にします。

EN ARPA LITEを併用する場合は、従来の
`YasouArpasingLitePhonemizer.dll`も残して構いません。名前と識別子が異なるため、
同時に導入できます。混在版のEN ARPA LITE v0.1.11は使用しないでください。

## 対応するVCCV歌詞

標準的なCz式CORE VCCVの次の形を自動判別します。

- 語頭CV：`-CV`
- CV：`CV`
- VC：`V C`
- 語尾VC：`VC-` / `V-`
- CC：`CC`
- 子音連鎖後のCV：`_CV`
- 母音連鎖：`VV`

| VCCV歌詞例 | ARPAsing側の処理例 |
|---|---|
| `-hE` | `- hh` + `hh iy` |
| `E l` | `iy l` |
| `lO` | `l ow` |
| `O -` | `ow -` |
| `V CC`相当 | `V C1` + `C1 C2` |
| `_CV` | `C V`として継続し、不要な語頭音を追加しない |

各VCCVノートは独立した音素断片として処理します。前後のノートから母音や語尾を
重複させません。直接のVC/CCが音源にない場合は、`V -` + `- C`、
`C1 -` + `- C2`のように分解して探索します。

対応する主なCz母音は
`a @ u x 0 8 I e 3 A i E O Q 6 o 9 & 1`、主な子音は
`b ch d dh f g h j k l m n ng/nk p r s sh t th v w y z zh dd`です。

## ARPAsing音源への適応

- 中央母音は`ah`を優先し、必要なエイリアスがなければ同じ位置の`aa`を探します。
- `ax`は`ah`へ正規化します。
- `dx`がなければ`d`、`nx`がなければ`n`を試します。
- 語頭、CV、VC、CC、語尾のARPAsingエイリアスを探索します。
- 一部のエイリアスがない軽量音源では、語尾・語頭へ分解して補います。

初回使用時、音源フォルダに`vccv-to-arpa.yaml`が作成されます。このファイルは
EN ARPA LITEの`arpasing-lite.yaml`とは独立しています。

## 既知の制限

- 標準的なCz式CORE VCCVを対象とします。Delta式、X-SAMPA式、独自記号を使う
  VCCVでは追加対応が必要です。
- 元UST/USTXのVCCV分割そのものが誤っている場合、フォネマイザーだけでは発音境界を
  完全には復元できません。
- 音源に必要なCVも分解先も存在しない箇所は、無音になる場合があります。
- `br`などCz式VCCV以外の特殊歌詞は、VCCV変換の対象外です。

## ビルド

.NET 8 SDKを使用します。

```sh
dotnet build src/VccvToArpaPhonemizer.csproj -c Release
```

生成物は`src/bin/Release/net8.0/VccvToArpaPhonemizer.dll`です。
コンパイル参照用の`OpenUtau.Core.dll`と`Serilog.dll`は`src/lib/`へ配置します。

## ライセンスと由来

フォネマイザー基盤はOpenUtauの`SyllableBasedPhonemizer`およびCadlaxa氏の
`ArpasingPlusPhonemizer`を元にしています。元コードはMIT Licenseです。
詳細は`OPENUTAU_LICENSE.txt`と`THIRD_PARTY_NOTICES.md`を参照してください。
