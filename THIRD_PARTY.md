# 外部依赖与再分发范围

v1.1.0 仅插件包只含本项目插件、使用说明、构建摘要、`SHA256SUMS.txt`、`LICENSE`、`OPENCC-APACHE-2.0.txt`、随时存档验收说明和本文档，不含游戏或加载器二进制。构建摘要不含本机依赖清单。本项目代码采用 [MIT 许可证](LICENSE)；外部依赖及其再分发范围见下表。

| 依赖 | 用途 | 获取与分发边界 |
|---|---|---|
| 《活侠传》及 Unity/Fungus 等游戏程序集 | 本地接口、UI、运行环境 | 从用户已安装游戏引用；不上传程序集、剧情资源、玩家存档或完整 API 转储 |
| BepInEx 6 Mono x64，验证 be.692 | 加载、配置 | 仅插件包不含加载器；便携安装器附带固定官方版本、许可证、署名与对应源码，详见 installer/third-party |
| 加载器提供的 `0Harmony.dll` | Harmony 补丁 | 由具体加载器提供，可能是 HarmonyX；不能仅凭程序集名认定版本、作者或许可 |
| Roslyn 4.12 与 .NET | 本地编译、离线测试 | 用户提供；不随插件包分发 |
| OpenCC 简繁字形表 | `src/Game/UiLanguage.cs` 内嵌从字形表筛选、重排的面板字形对照 | [OpenCC](https://github.com/BYVoid/OpenCC)，Apache-2.0；许可证全文见 [OPENCC-APACHE-2.0.txt](OPENCC-APACHE-2.0.txt)。插件运行时不加载 OpenCC 库或完整字典 |

官方项目定位：BepInEx（github.com/BepInEx/BepInEx）、HarmonyX（github.com/BepInEx/HarmonyX）、Harmony（github.com/pardeike/Harmony）、Roslyn（github.com/dotnet/roslyn）。这些定位信息不能替代对实际二进制版本的检查。

`tools/package.py` 只产出插件包，不接受 `--loader`；`tools/build_installer.py` 构建固定 be.692 的离线安装器，并核对加载器归档与对应源码哈希。许可材料和来源记录见 [安装器第三方说明](installer/third-party/THIRD-PARTY-NOTICES.md) 与 [核验清单](installer/third-party/manifest.json)。
