# 本轮验证记录

日期：2026-10-19。结论：**编译、离线合同检查与启动级实机验证通过；交互路径、辅助效果和恢复闭环仍未验证，不能发布为完整替代版。**

## 构建

- 本机游戏：Steam Build 26021712；引用 `Mortal_Data/Managed`，未复制游戏程序集进工程。
- 加载器编译引用：BepInEx 6 Mono be.692 的 core 目录；未分发框架。
- 编译器：Roslyn 4.12，运行于 .NET 8；开启警告视为错误。
- 产物：`artifacts/MortalHelper.dll`。
- SHA-256：`f99d838bf15a161b496ef4cfa4d515fd0b862c53f9f67ef6ad62dab92ed2297d`。
- Python 构建脚本和源码检查脚本通过语法检查。

## 离线行为检查

`tests/Contracts.cs` 执行 **237 项断言**，0 失败。该数量包含对恢复文件每个截断位置的循环检查，不代表 237 个独立游戏场景。

本机复核（2026-10-19 第二轮）：在受限沙箱下 `File.Replace` 会被拒绝，旧实现因此在这一步抛 `UnauthorizedAccessException`，最后一组存档槽断言跑不完。`ArchiveStore` 现按顺序处理三种情况：替换不可用时回退为“先复制旧主档到 `.bak`、再覆盖主档”；主档已损坏时不得提升为备份，保留更早的可用备份；测试用编译期接缝强制走回退分支。三者在同一份合同测试内覆盖，因此本机在受限环境下也能把断言跑满。

覆盖：

- 关闭/过期会话、重复提交、合法和非法点数、直接选项优先、结果清理、重掷时机。
- Lua 条件语句、未知调用、非字面量、长注释和字符串、重复标识、未闭合字符串、过大输入、伪造行内标识。
- 格式往返、全部截断位置、内容篡改、尾随数据、伪造正确哈希后的未知类型/超大字段、非法数值、错误场景、字段预算。
- 客户端不匹配、恢复期间修改、跳过校验、恢复过程中替换记录、源对象突变、事件错位、失败后锁定。
- 未完成交互不作为完成事件、非停点拒绝保存、嵌套交互/随机拒绝、过期完成通知、失败的新基线清除旧基线。
- 主档替换和上一版本备份、非法槽号、损坏主档拒绝、显式备份读取。
- 替换不可用时的复制回退（强制走回退分支后主档更新、备份保留）、损坏主档不会被写成备份、覆盖损坏主档后旧备份仍可读取。
- 恢复控制器的阶段时序：原生基线先于重放加载；停点交互不会被当成重放事件消费；乱序、恢复后状态不符、基线加载失败、显式取消都只回调一次适配器中止并锁住操作；失败后必须显式确认才解锁。

所有测试使用人工数据与临时文件夹，没有读取或修改玩家存档。未把旧工程的测试数字计入本报告。

## 二进制与源码检查

实际读取了新 DLL 的程序集引用：包含本机游戏、Unity、BepInEx、Harmony 与基础运行库；没有 `LOMSave2`、`MortalFateHelper` 或 DiceMaster 程序集引用。

`tools/check_source.py` 检查源码和文档目录没有游戏/旧插件二进制、Lua 剧情资产、旧命名空间或 BinaryFormatter 代码。启动冲突检测中的旧 DLL 文件名是提示信息，不是依赖。

这类检查能发现明显的错误打包或依赖回流，不能独自证明全部代码的法律独立性。

`tools/provenance_scan.py` 对 17 个新 C# 文件与旧仓库 91 个源文件做了 token shingle 与最长公共片段比对：最高包含率 10.8%，且最长公共片段是 Harmony 目标签名；其余集中在 `using` 指令、Unity UI 赋值和“写临时文件再替换”惯用法。逐文件结果见 [PROVENANCE.md](../PROVENANCE.md)。

## 实机启动验证（2026-10-19）

安装位置：`D:\steam\steamapps\common\LegendOfMortal`。游戏 Unity 2020.3.49f1，加载器 BepInEx 6.0.0-be.692（Mono/x64），手工放入 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 与 `BepInEx\{core,config,plugins}`；新 DLL 位于 `BepInEx\plugins\cerveth.mortal.helper\MortalHelper.dll`。安装前该目录没有任何加载器文件；本工程未读写玩家存档目录。

运行 1（`plugins` 内还留着旧整合版的四个插件）：本插件在 `Awake` 检测到旧插件后抛错停用，日志为 `System.InvalidOperationException: 检测到旧辅助插件 LOMSave2.dll；请先将其移出 plugins 后重启。`——启动冲突保护按设计生效。

运行 2（`plugins` 内只留本工程 DLL）：`1 plugin to load`，出现 `Independent helper 0.1.0 initialized; experimental build.` 与 `Chainloader startup complete`，没有补丁错误、没有运行期异常。16 个 `[HarmonyPatch]` 目标全部解析成功（`DiceMenuDialog`、`CheckPointManager`、`LuaEnvironment`、`NormalOptionButton`、`DiceOption`、`CombatStatController`、`CombatDialogUI`、`CombatManager`、`SaveSystem`）。

界面：向游戏窗口发送 Tab 后截图可见面板标题「活侠助手 · 独立重建 0.1.0」、启用复选框、剧情/单挑/属性/关闭标签页、轮盘与效果提示选项，中文正常渲染、无豆腐块。截图含游戏画面，只作本地证据、不进仓库（`outputs\实机验证-2026-10-19\`）。

仍待人工验证：面板拖动缩放、点击标签页、轮盘在真实判定中的行为、提示文本正确性、单挑辅助效果、原生菜单与键盘输入共存，以及剧情/单挑恢复闭环。

第二轮实机复核（2026-10-19，同一台机器）：

- 插件新增画面与面板诊断日志。启动时报 `Display: visible=False bounds=36,64 640x580 screen=294x169`，面板首次绘制时报 `Hidden affordance ... screen=960x540`——`Awake` 时的 `Screen` 只是占位值，真实渲染分辨率是 960x540，窗口像素 493x306（DPI 缩放约 1.68）。
- 连续两次 Tab 的日志为 `Panel toggled: visible=True` 与 `Panel toggled: visible=False`，展开与收起两个方向都真实生效。其中一次日志为 `bounds=36,0 640x540`：640x580 的默认高度被压到屏幕高度并把面板顶到 y=0。
- 已改：面板在 `Screen` 变化时按 96% 画面重新适配，不再无条件顶到 y=0；收起状态的重开入口改成不依赖游戏皮肤与中文字体的半透明色块加 ASCII 文字（启动早期拿不到 CJK 字体时，原中文按钮会渲染成不可见）；`OnGUI` 异常现在会写一次插件日志，此前只进 Unity 的 `Player.log`。
- 方法学限制：`PrintWindow` 对这个 DXGI 窗口经常返回陈旧帧（连续三帧同 SHA、状态已变而画面未变），本轮不再把截图当证据，只以插件日志与人工确认为准。自动输入同样只在游戏拿到前台焦点时有效（`AttachThreadInput + keybd_event` 成功过一次）；用户正在使用桌面，反复抢焦点不可接受，交互验证改为人工步骤。

## 未覆盖

- 未验证原生轮盘在真实判定中的布局与回调顺序、命运点消耗、动画连点与场景切换。
- 未验证面板收起/拖动/缩放、点击标签页、按键与输入框共存。
- 未验证单挑辅助实际效果、剧情强制结果和原生菜单输入共存。
- 剧情/单挑恢复适配尚未完成（只有 `IReplayHost` 的测试假实现），因此没有真实存读闭环或旧附加档迁移。
- 未创建公开 GitHub 仓库，未发布 Release。
