# 外部依赖与再分发范围

0.1.1 仅插件包只含本项目插件、使用说明、构建摘要和本文档，不含游戏或加载器二进制。MIT 声明与来源待确认项见 [PROVENANCE.md](PROVENANCE.md)。

| 依赖 | 用途 | 获取与分发边界 |
|---|---|---|
| 《活侠传》及 Unity/Fungus 等游戏程序集 | 本地接口、UI、运行环境 | 从用户已安装游戏引用；不上传程序集、剧情资源、玩家存档或完整 API 转储 |
| BepInEx 6 Mono x64，历史验证 be.692 | 加载、配置 | 用户自行安装官方 BepInEx；插件包不附带加载器 |
| 加载器提供的 `0Harmony.dll` | Harmony 补丁 | 由具体加载器提供，可能是 HarmonyX；不能仅凭程序集名认定版本、作者或许可 |
| Roslyn 4.12 与 .NET | 本地编译、离线测试 | 用户提供；不随插件包分发 |

官方项目定位：BepInEx（github.com/BepInEx/BepInEx）、HarmonyX（github.com/BepInEx/HarmonyX）、Harmony（github.com/pardeike/Harmony）、Roslyn（github.com/dotnet/roslyn）。这些定位信息不能替代对实际二进制版本的检查。

历史 v0.1.0 整合包附带加载器；旧包的再分发义务仍应另行审查。当前打包器暂时拒绝 `--loader`：恢复整合包前应核对 BepInEx、Doorstop、HarmonyX 与其余实际组件的版本、许可证全文、署名及适用的对应源码供应要求。仅写一个上游仓库地址不视为已完成这些工作。
