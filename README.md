# PMX Editorプラグイン：物理チェーン自動調整

## 概要

PMX Editor で使う物理チェーン自動調整プラグインです。
プラグイン作り方全然わからんかったので大半のコードはAIに書かせました。

## ビルド方法

ビルドのしかたも当初全然わからんかったのでメモしておきます。Visual Studioは導入済みとしてます。

1. マイクロソフトのサイトから .NET Framework 4.8 SDK をインストールします（必須）。
2. `PhysicsChainAdjuster/PhysicsChainAdjuster.Local.props.example` をコピーして、`PhysicsChainAdjuster/PhysicsChainAdjuster.Local.props` を作成します。
3. `PhysicsChainAdjuster.Local.props` に、お手元の PMX Editor へのパス（`OutputPath` / `PEPluginPath` / `SlimDXPath`）を記入します（正しく設定できればコード中のシンボル未解決エラーが消え、コンパイルできるようになります）。
4. `PmxePlugin.sln` を Visual Studio で開き、`Release | x64` でビルドします。VS Codeでターミナルから `dotnet build` してもいいです。

慣れてる人は好きにしてください。

## ライセンス

[MIT License](http://opensource.org/licenses/MIT)

## 作者

Kafuji Sato
[X](https://X.com/kafuji)
[GitHub](https://github.com/kafuji)
