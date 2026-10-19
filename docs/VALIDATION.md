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

## 恢复基线自动探测（2026-10-19）

为回答「记录下来的基线能不能原样载回」，在插件里加了一次性自检（Diagnostics/SelfTest，默认关闭），不需要人工操作：

1. 用游戏读档面板的同一套调用顺序载入真实存档：SetSlot("003") → SoundManager.StopMusic() → LoadGameData() → SaveUniverseData() → MissionManagerData.UpdateCheckMissions() → SceneController.LoadCurrentScene()；
2. 10 秒后取 SaveSystem.GetCurrentSaveData()（下称 A），并用播放路径重新应用：JsonUtility.FromJson<GameSave>(A) → 私有 ExecuteLoadGameData → SaveUniverseData → UpdateCheckMissions → LoadCurrentScene；
3. 0.5 秒后取 B，逐字段比较 A 与 B。

结果：

- 原生读档后场景变为 **Story**，说明这套调用顺序确实能把玩家带进存档对应的剧情位置；
- 重新应用后场景仍是 **Story**；
- A 与 B 逐字段比较只剩 **5 处差异**：FreeRandomState 的 4 个字段与 TimeTick；
- **剧情位置字段完全一致**：CurrentStoryScript、CurrentScene、CurrentSceneKey、CurrentNextScene、临时脚本、属性、物品、商店库存、关系全部相同。

结论：

1. 「存档 JSON 作基线」是可行的：ExecuteLoadGameData 能把数据原样装回，且没有丢字段；
2. **只调用 ExecuteLoadGameData 不会切场景**，必须补 SaveUniverseData + UpdateCheckMissions + LoadCurrentScene（读档面板就是这么做的）；
3. TimeTick 每次应用都会刷新为当前时间（CreateSaveData 里写 DateTime.Now.Ticks），比较时必须忽略；
4. FreeRandomState（自由场景随机数）不会被还原成记录值，重放时不能假设随机一致——要么显式写回该字段，要么让重放不依赖自由场景随机。

第一次测量（4 秒窗口）曾出现物品与商店库存差异，把窗口缩到 0.5 秒后消失，说明那是这几秒里剧情真的在跑，不是还原丢失。

探测代码保留为 Diagnostics/SelfTest（默认关闭，开启后仅在失焦时也需要运行时才会设置 Application.runInBackground），可作为后续回归工具。
## 恢复播放接线（2026-10-19）

按基线探测结论把播放侧接到 RecoveryController（方案 A）：

- 记录键改为可复现标识：选项 C␁脚本名␁选项文本，判定 D␁脚本名␁脚本内第 N 次判定；
- StoryReplay 不再自己维护游标：Start 走 RecoveryController.Start（预检 → Host.LoadBaseline → 重放），事件用 TryConsume(kind,key) 消费，key 不符立即中止；
- Host.LoadBaseline 按实测得到的读档流程执行：JsonUtility.FromJson → 私有 ExecuteLoadGameData → SaveUniverseData → UpdateCheckMissions → LoadCurrentScene；
- 新增 RecoveryController.VerifyAtEnd()：没有停点交互的记录在重放结束后直接比对状态摘要；失败锁门并回调中止；
- 每次重放结束后重算摘要，TimeTick 不参与比较（探测已证明它每次应用都会刷新）；
- 场景切换与 120 秒停滞仍会中止重放。

离线验证：新增 6 条断言（VerifyAtEnd 的时序、完成标志、摘要不一致中止），累计 **248 项** 通过。

仍未验证：真实剧情中的注入顺序与停点行为；旧 .mhr 记录的键是自增序号，与新键不兼容（格式版本未变，但恢复会因 key 不符直接中止，不会静默错位）。
## 无头功能检测（2026-10-19，不移动鼠标/不抢焦点）

用 Probe.cs（配置项 Diagnostics/SelfTest，默认关闭）在游戏进程内做检测：只调用游戏与插件 API、程序化派发按钮事件，不使用任何 Win32 鼠标/焦点接口。

| 项目 | 结果 |
|---|---|
| 存档备注写入 + 文件回读 | PASS |
| 分支记录写入/查询/文件 | PASS |
| 真实菜单上派发 pointerClick 后「已走」记录 | PASS（ecorded=True） |
| 游戏速度 2 倍 / 恢复 1 倍 | PASS |
| 存档格数写入生效 | PASS |
| 控骰配置（Mode=1 + Point="37"） | PASS |
| **指定点数注入**：ange=0..99 时注入 37 | **PASS（掷出 37）** |
| 选项提示：真实剧本 S8400_01/02 | 解析出 属性变化：mental，-20 / disposition，2 |
| 后果扫描 | 解析出 剧情 S8400_02_001 |
| 剧情自动推进 | 25+ 菜单、96+ 条提示 |

第二轮骰子测试记为 FAIL，原因是 Point 配置在两次判定之间被清空（探测自身的状态管理问题），不是注入缺陷。

同时修正一个真实缺陷：记录骰子时存的是 DiceCheckResult.Result（含加成后的结果），而重放注入的是 Random（原始点数）；现改为记录 Random。

仍未覆盖：单挑/群战辅助（需进战斗）、养成辅助（需练功/锻造操作）、存档面板实际槽位与 F2/F3、数值编辑落值、重放注入全链路。