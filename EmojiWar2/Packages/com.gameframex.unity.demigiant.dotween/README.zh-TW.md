<div align="center">

<img src="https://download.alianblank.com/gameframex/gameframex_logo_320.png" alt="Game Frame X Logo" width="160" />

# Game Frame X DOTween

[![License](https://img.shields.io/github/license/GameFrameX/com.gameframex.unity.demigiant.dotween)](https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/blob/main/LICENSE.md)
[![Version](https://img.shields.io/github/v/release/GameFrameX/com.gameframex.unity.demigiant.dotween)](https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/releases)
[![Unity Version](https://img.shields.io/badge/Unity-2019.4-black?logo=unity)](https://unity.com/)
[![Documentation](https://img.shields.io/badge/Documentation-docs-blue)](https://gameframex.doc.alianblank.com)

獨立遊戲前後端一體化解決方案 · 獨立遊戲開發者的圓夢大使

<br />

[文檔](https://gameframex.doc.alianblank.com) · [快速開始](#快速開始) · QQ群: 467608841 / 233840761

<br />

[English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文** | [日本語](README.ja.md) | [한국어](README.ko.md)

</div>

## 項目簡介

Game Frame X DOTween 是 [DOTween](http://dotween.demigiant.com) 的 GameFrameX 框架整合包。DOTween 是一個 Unity 動畫插件，用於建立補間動畫。

該庫主要服務於 [GameFrameX](https://github.com/AlianBlank/GameFrameX) 作為子庫使用。

## 改動功能

1. 增加 `link.xml` 的裁剪過濾

## 快速開始

### 安裝

選擇以下任一方式：

1. 編輯 Unity 專案的 `Packages/manifest.json`，添加 `scopedRegistries` 部分：
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

   `scopes` 控制哪些套件透過此註冊表解析。只有以 `com.gameframex` 開頭的套件才會從這個註冊表取得。

2. 直接在 `manifest.json` 的 `dependencies` 節點下添加以下內容：
   ```json
   {
      "com.gameframex.unity.demigiant.dotween": "https://github.com/gameframex/com.gameframex.unity.demigiant.dotween.git"
   }
   ```
3. 在 Unity 的 `Package Manager` 中使用 `Git URL` 的方式添加庫，地址為：`https://github.com/gameframex/com.gameframex.unity.demigiant.dotween.git`
4. 直接下載倉庫放置到 Unity 專案的 `Packages` 目錄下，會自動載入識別。
## 使用方法

- 匯入後，從 "Tools/Demigiant" 選單中選擇 DOTween 的 Utility Panel，按下 "Setup DOTween..." 按鈕來啟用/停用模組。
- 在程式碼中，在每個需要使用 DOTween 的類別中添加 `using DG.Tweening`。

## 文檔與資源

- DOTween 文檔: http://dotween.demigiant.com/documentation.php
- DOTween 許可證: http://dotween.demigiant.com/license.php
- GameFrameX 文檔: https://gameframex.doc.alianblank.com
- 倉庫地址: https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween
- 問題回報: https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/issues


## 依賴

| 套件 | 說明 |
|------|------|
| (无) | - |


## 社區與支援

- QQ群: 467608841 / 233840761

## 更新日誌

查看 [Releases](https://github.com/GameFrameX/gameframex/com.gameframex.unity.demigiant.dotween/releases) 了解更新日誌。
## 開源協議

DOTween 和 DOTween Pro 版權所有 (c) 2014-2018 Daniele Giardini - Demigiant。詳細資訊請查看 [LICENSE](LICENSE.md) 檔案。
