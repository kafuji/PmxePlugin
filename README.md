# PMX Editorプラグイン：物理チェーン自動調整

## 概要

PMX Editor で使う物理チェーン自動調整プラグイン＆新しいプラグインつくるの楽なフレームワーク的なやつです。
新しいプラグインを作るための雛形として`HelloPlugin`も同梱しています。
プラグイン作り方全然わからんかったので大半のコードはAIに書かせました。

## ビルド方法

ビルドのしかたも当初全然わからんかったのでメモしておきます。Visual Studioは導入済みとしてます。

1. マイクロソフトのサイトから .NET Framework 4.8 SDK をインストールします（必須）。
2. `Local.props.example` をコピーして、リポジトリルートに `Local.props` を作成します。
3. `Local.props` に、お手元の PMX Editor へのパス（`OutputPath` / `PEPluginPath` / `SlimDXPath`）を記入します（正しく設定できればコード中のシンボル未解決エラーが消え、コンパイルできるようになります）。
4. `PmxePlugin.sln` を Visual Studio で開き、`Release | x64` でビルドします。VS Codeでターミナルから `dotnet build` してもいいです。

慣れてる人は好きにしてください。

## 新しいプラグインの追加方法

1. ルート直下に新しいディレクトリを作成し、`<PluginName>.csproj` とコードを配置します。
2. `csproj` では `..\\Local.props` を Import して、`PEPluginPath` などの共通設定を参照します。
3. `dotnet sln PmxePlugin.sln add <PluginName>/<PluginName>.csproj` でソリューションに追加します。
4. 追加したプロジェクトを通常ビルド対象にしたくない場合は、Visual Studio の構成マネージャーで Build を外すか、`PmxePlugin.sln` の `ProjectConfigurationPlatforms` から対象プロジェクトの `*.Build.0` 行を削除します。

`HelloPlugin` は雛形用プロジェクトとしてソリューションには含めていますが、デフォルトではビルド対象から外しています。

## ライセンス

[MIT License](http://opensource.org/licenses/MIT)

## 作者

Kafuji Sato
[X](https://X.com/kafuji)
[GitHub](https://github.com/kafuji)
