# v0.1.1
- OpenUtau v0.1.569.0 API / .NET 10対応。
- toneShiftのnullable化とトラック属性の継承へ対応。
- 公式0.1.569-beta本体DLLによる外部読み込みと合成OTO回帰試験に合格。

# Changelog

## 0.1.0 - 2026-08-23

- EN ARPA LITEから独立した`VCCVtoARPA`として初版を作成。
- DLL名を`VccvToArpaPhonemizer.dll`、OpenUtau識別子を`EN VCCV2ARPA`へ分離。
- 設定ファイルを専用の`vccv-to-arpa.yaml`へ分離。
- Cz式英語VCCVの`-CV`、`CV`、`V C`、`VC-`、`CC`、`_CV`、`VV`を
  ARPAbetへ変換。
- 変換済み断片をARPAsingのCV・VC・CC・語頭・語尾へ割り当て。
- 欠けたVC・CCの分解と、`ah`優先・`aa`フォールバックに対応。
