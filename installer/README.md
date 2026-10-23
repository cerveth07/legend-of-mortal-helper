# 便携安装器

`MortalHelper-Setup.exe` 集成插件和固定版本 BepInEx；双击后自动查找 Steam 游戏目录，也可选择 `Mortal.exe`。点击「安装 / 更新」，从 Steam 正常启动游戏，按 Tab 打开万象录。安装器无需常驻，用完即可删除；以后重新下载相同安装器可以卸载。

- Windows 10 / 11，依赖系统自带 .NET Framework 4.7.2 或更新版本。无需额外安装现代 .NET Runtime，不注册服务、不申请管理员权限。
- 游戏运行时拒绝安装或卸载。现有 BepInEx 6 Mono x64 保持原样，版本不兼容时解释原因并停止。
- 更新保留设置；接管手工安装的旧插件前，备份至 `BepInEx/mortal-helper-backup/`。
- 安装记录为 `BepInEx/mortal-helper-install.xml`。卸载仅删除安装器拥有且内容未变的文件。存在其他插件/补丁或加载器文件被修改时，保留共用加载器。游戏存档、插件设置与日志不删除。
- 通过「打开游戏目录」可查看文件。若卸载后仍有目录，通常是保留的设置、日志、备份或其他 Mod。
- 当前 exe 未做代码签名，Windows 可能显示来源提示。发布时同时提供 SHA256 文件。

开发构建在 WSL 中运行：

```bash
python3 tools/build_installer.py --plugin artifacts/MortalHelper.dll \
  --loader-zip /path/to/BepInEx-be692-x64.zip \
  --out dist/MortalHelper-Setup.exe --test
```

构建复用插件打包校验，拒绝过期、未测或诊断版本。依赖 `installer/third-party/manifest.json` 指定的精确加载器归档及其许可证材料；许可证未审核完毕时拒绝打包。

`BackendTests.cs` 在临时伪游戏目录测试实际安装/卸载逻辑，不写入真实游戏，不启动游戏。游戏内新增功能由用户自行验收。

界面离屏检查（不显示或激活窗口、不安装文件、不操作游戏）：

```text
MortalHelper-Setup.exe --smoke-ui C:\path\installer-preview.png
```

此模式使用 WinForms `DrawToBitmap` 渲染当前系统 DPI 下的原生控件。窗口采用 DPI 缩放与固定最小尺寸。
