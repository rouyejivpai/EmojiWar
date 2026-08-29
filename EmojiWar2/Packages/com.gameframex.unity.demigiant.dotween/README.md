<div align="center">

<img src="https://download.alianblank.com/gameframex/gameframex_logo_320.png" alt="Game Frame X Logo" width="160" />

# Game Frame X DOTween

[![License](https://img.shields.io/github/license/GameFrameX/com.gameframex.unity.demigiant.dotween)](https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/blob/main/LICENSE.md)
[![Version](https://img.shields.io/github/v/release/GameFrameX/com.gameframex.unity.demigiant.dotween)](https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/releases)
[![Unity Version](https://img.shields.io/badge/Unity-2019.4-black?logo=unity)](https://unity.com/)
[![Documentation](https://img.shields.io/badge/Documentation-docs-blue)](https://gameframex.doc.alianblank.com)

All-in-One Solution for Indie Game Development · Empowering Indie Developers' Dreams

<br />

[Documentation](https://gameframex.doc.alianblank.com) · [Quick Start](#quick-start) · QQ Group: 467608841 / 233840761

<br />

**English** | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

</div>

## Project Overview

Game Frame X DOTween is a [DOTween](http://dotween.demigiant.com) integration package for the GameFrameX framework. DOTween is a Unity animation plugin for creating tween animations.

This library primarily serves as a submodule for [GameFrameX](https://github.com/AlianBlank/GameFrameX).

## Changes from Upstream

1. Added `link.xml` stripping filter

## Quick Start

### Installation

Choose one of the following methods:

1. Edit your Unity project's `Packages/manifest.json` and add the `scopedRegistries` section:
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

   `scopes` controls which packages are resolved through this registry. Only packages whose names start with `com.gameframex` will be fetched from it.

2. Add to `manifest.json` dependencies:
   ```json
   {
      "com.gameframex.unity.demigiant.dotween": "https://github.com/gameframex/com.gameframex.unity.demigiant.dotween.git"
   }
   ```
3. Use **Package Manager** in Unity with **Git URL**: `https://github.com/gameframex/com.gameframex.unity.demigiant.dotween.git`
4. Clone the repository into your Unity project's `Packages` directory. It will be loaded automatically.
## Usage Examples

- After importing, select DOTween's Utility Panel from the "Tools/Demigiant" menu and press "Setup DOTween..." to activate/deactivate Modules.
- In your code, add `using DG.Tweening` to each class where you want to use DOTween.

## Documentation & Resources

- DOTween Documentation: http://dotween.demigiant.com/documentation.php
- DOTween License: http://dotween.demigiant.com/license.php
- GameFrameX Documentation: https://gameframex.doc.alianblank.com
- Repository: https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween
- Issues: https://github.com/GameFrameX/com.gameframex.unity.demigiant.dotween/issues


## Dependencies

| Package | Description |
|---------|-------------|
| (无) | - |


## Community & Support

- QQ Group: 467608841 / 233840761

## Changelog

See [Releases](https://github.com/GameFrameX/gameframex/com.gameframex.unity.demigiant.dotween/releases) for changelog.
## License

DOTween and DOTween Pro are copyright (c) 2014-2018 Daniele Giardini - Demigiant. See [LICENSE](LICENSE.md) for details.
