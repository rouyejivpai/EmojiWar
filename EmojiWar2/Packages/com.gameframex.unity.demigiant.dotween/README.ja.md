<div align="center">

<img src="https://download.alianblank.com/gameframex/gameframex_logo_320.png" alt="Game Frame X Logo" width="160" />

# Game Frame X DOTween

[![License](https://img.shields.io/github/license/GameFrameX/com.gameframex.unity.demigiant.dotween)](https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/blob/main/LICENSE.md)
[![Version](https://img.shields.io/github/v/release/GameFrameX/com.gameframex.unity.demigiant.dotween)](https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/releases)
[![Unity Version](https://img.shields.io/badge/Unity-2019.4-black?logo=unity)](https://unity.com/)
[![Documentation](https://img.shields.io/badge/Documentation-docs-blue)](https://gameframex.doc.alianblank.com)

インディゲーム開発者向けオールインワンソリューション · インディ開発者の夢を支援

<br />

[ドキュメント](https://gameframex.doc.alianblank.com) · [クイックスタート](#クイックスタート) · QQグループ: 467608841 / 233840761

<br />

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | **日本語** | [한국어](README.ko.md)

</div>

## プロジェクト概要

Game Frame X DOTween は [DOTween](http://dotween.demigiant.com) の GameFrameX フレームワーク統合パッケージです。DOTween はトゥイーンアニメーション作成用の Unity アニメーションプラグインです。

このライブラリは主に [GameFrameX](https://github.com/AlianBlank/GameFrameX) のサブモジュールとして使用されます。

## 変更内容

1. `link.xml` ストリッピングフィルターを追加

## クイックスタート

### インストール

以下のいずれかの方法を選択してください：

1. Unity プロジェクトの `Packages/manifest.json` を編集し、`scopedRegistries` セクションを追加してください：
   ```json
   {
     "scopedRegistries": [
       {
         "name": "GameFrameX",
         "url": "https://gameframex.upm.alianblank.uk",
         "scopes": [
           "com.gameframex"
         ]
       }
     ],
     "dependencies": {
       "com.gameframex.unity.demigiant.dotween": "1.1.1"
     }
   }
   ```

   `scopes` は、どのパッケージをこのレジストリから解決するかを制御します。`com.gameframex` で始まるパッケージのみがこのレジストリから取得されます。

2. `manifest.json` の `dependencies` に直接追加：
   ```json
   {
      "com.gameframex.unity.demigiant.dotween": "https://github.com/gameframex/com.gameframex.unity.demigiant.dotween.git"
   }
   ```
3. Unity の **Package Manager** で **Git URL** を使用して追加：`https://github.com/gameframex/com.gameframex.unity.demigiant.dotween.git`
4. リポジトリを Unity プロジェクトの `Packages` ディレクトリにクローンしてください。自動的に読み込まれます。
## 使用方法

- インポート後、"Tools/Demigiant" メニューから DOTween の Utility Panel を選択し、"Setup DOTween..." ボタンを押してモジュールの有効化/無効化を行います。
- コード内で DOTween を使用する各クラスに `using DG.Tweening` を追加します。

## ドキュメントとリソース

- DOTween ドキュメント: http://dotween.demigiant.com/documentation.php
- DOTween ライセンス: http://dotween.demigiant.com/license.php
- GameFrameX ドキュメント: https://gameframex.doc.alianblank.com
- リポジトリ: https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween
- イシュー: https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/issues


## 依存関係

| パッケージ | 説明 |
|----------|------|
| (无) | - |


## コミュニティとサポート

- QQグループ: 467608841 / 233840761

## 変更履歴

[Releases](https://github.com/GameFrameX/gameframex/com.gameframex.unity.demigiant.dotween/releases) で変更履歴を確認してください。
## ライセンス

DOTween and DOTween Pro are copyright (c) 2014-2018 Daniele Giardini - Demigiant. 詳細は [LICENSE](LICENSE.md) をご覧ください。
