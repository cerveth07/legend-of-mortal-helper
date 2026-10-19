# 外部依赖

当前源码包不分发依赖二进制。

| 依赖 | 用途 | 处理方式 |
|---|---|---|
| 《活侠传》与其 Unity/Fungus 等程序集 | 游戏接口、UI、运行环境 | 从用户本机安装引用，不上传程序集、剧情资产或生成的完整 API 转储 |
| BepInEx 6 Mono | 插件加载与配置 | 用户提供；对应来源为 [BepInEx](https://github.com/BepInEx/BepInEx)，版本许可需与实际加载器配套 |
| Harmony | 方法拦截 | 本地加载器提供 `0Harmony.dll`，来源为 [Harmony](https://github.com/pardeike/Harmony) |
| Roslyn 4.12 | 构建工具 | 用户提供 `csc.dll`；来源为 [Roslyn](https://github.com/dotnet/roslyn)，不随插件分发 |

最终若提供含加载器的安装包，必须另补逐版本的许可证、署名及适用的源码供应说明；不能把这张表当作已完成二进制再分发义务。

没有把 DiceMaster_Fix.dll、LOMSave2.dll、知天命 DLL、旧插件资源或旧测试夹具带入本工程。启动冲突检查中出现旧 DLL 名称，仅用于提示用户移除冲突安装，并不是编译或运行依赖。
