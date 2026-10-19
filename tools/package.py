"""Builds the drop-in packages.

Two layouts are produced from the same compiled plugin:

  full   loader (winhttp.dll + BepInEx/core) plus the plugin, so copying the
         folder contents into the game directory is enough to make it work;
  plugin only the plugin, for players who already run BepInEx 6 Mono.

Nothing from the game is copied: only the loader files provided locally and our
own DLL. No network access, no installation into the game directory.
"""

import argparse
import hashlib
import os
import shutil
import zipfile
from pathlib import Path

VERSION = "0.1.0"
NAME = "活侠万象录"

USAGE = """活侠·万象录 {version} —— 使用说明
================================================

这是一个《活侠传》的整合 Mod。安装不需要安装器：把本文件夹里的内容，
整体复制到游戏根目录（和 Mortal.exe 同一个文件夹）即可。

安装步骤
--------
1. 完全退出游戏。
2. 备份存档目录（通常在 %USERPROFILE%\\AppData\\LocalLow\\Obb Studio\\Mortal\\<你的SteamID>）。
3. 把本文件夹里的全部内容复制进游戏根目录，允许合并。
4. 从 Steam 启动游戏，进入游戏后按 Tab 打开面板。

复制后游戏根目录应当是：
    Mortal.exe
    winhttp.dll
    doorstop_config.ini
    .doorstop_version
    BepInEx\\core\\...
    BepInEx\\plugins\\cerveth.mortal.helper\\MortalHelper.dll

确认是否生效
------------
打开 BepInEx\\LogOutput.log，应能看到：
    活侠·万象录 0.1.0  初始化完成

卸载
----
删除 winhttp.dll、doorstop_config.ini、.doorstop_version 和整个 BepInEx 文件夹。
存档不受影响；面板里写入的数值不会自动撤销。

注意事项
--------
- 如果游戏目录里已有 BepInEx（其他 Mod 自带），不要直接覆盖：先确认版本，
  或改用「仅插件」包，只放 MortalHelper.dll。
- 启动时若检测到 MortalFateHelper.dll、LOMSave2.dll、DiceMaster*.dll、
  ControlMyDestiny.dll 等旧插件，本插件会自行停用并在日志里报冲突，
  请先把它们移出 plugins 目录。
- 不要在游戏运行时替换文件。
"""

NOTICE = """第三方组件说明
================================================

本整合包内含的加载器二进制来自 BepInEx 项目：
  BepInEx 6.0.0-be.692 (Unity Mono, x64) — https://github.com/BepInEx/BepInEx
  包含 winhttp.dll（Doorstop 注入器）、doorstop_config.ini、BepInEx/core 下的程序集。

BepInEx 采用 LGPL-2.1 许可；其完整许可文本与对应源码见上述官方仓库。
本包不修改这些二进制，只按原样分发。若你需要源码或不同许可条款下的版本，
请直接从官方发布页获取。

本包不包含《活侠传》的游戏程序集、剧情资源、存档，也不包含任何旧版
第三方 Mod（骰子大师、LOMSave 等）的代码或二进制。

本包中的插件（MortalHelper.dll 及其源码）由本工程使用 MIT 许可。
"""


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def copy_tree(source: Path, target: Path, root: Path) -> None:
    for item in sorted(source.rglob("*")):
        if item.is_dir():
            continue
        relative = item.relative_to(root)
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, destination)


def write_zip(folder: Path, archive: Path) -> None:
    if archive.exists():
        archive.unlink()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        for item in sorted(folder.rglob("*")):
            if item.is_file():
                bundle.write(item, item.relative_to(folder).as_posix())


def main() -> int:
    parser = argparse.ArgumentParser(description="Build drop-in packages")
    parser.add_argument("--plugin", required=True, help="compiled MortalHelper.dll")
    parser.add_argument("--loader", help="folder with winhttp.dll and BepInEx/ for the full pack")
    parser.add_argument("--out", default="dist", help="output directory")
    parser.add_argument("--version", default=VERSION)
    args = parser.parse_args()

    plugin = Path(args.plugin).resolve()
    if not plugin.is_file():
        raise SystemExit("plugin not found: %s" % plugin)
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    made = []
    plugin_root = out / ("%s-%s-plugin" % (NAME, args.version))
    if plugin_root.exists():
        shutil.rmtree(plugin_root)
    (plugin_root / "BepInEx" / "plugins" / "cerveth.mortal.helper").mkdir(parents=True)
    shutil.copy2(plugin, plugin_root / "BepInEx" / "plugins" / "cerveth.mortal.helper" / "MortalHelper.dll")
    (plugin_root / "使用说明.txt").write_text(USAGE.format(version=args.version), encoding="utf-8")
    (plugin_root / "第三方组件说明.txt").write_text(NOTICE, encoding="utf-8")
    make_sums(plugin_root)
    write_zip(plugin_root, out / ("%s-%s-plugin.zip" % (NAME, args.version)))
    made.append(plugin_root)

    if args.loader:
        loader = Path(args.loader).resolve()
        full_root = out / ("%s-%s-full" % (NAME, args.version))
        if full_root.exists():
            shutil.rmtree(full_root)
        full_root.mkdir(parents=True)
        for name in ("winhttp.dll", "doorstop_config.ini", ".doorstop_version"):
            source = loader / name
            if not source.is_file():
                raise SystemExit("loader file missing: %s" % source)
            shutil.copy2(source, full_root / name)
        copy_tree(loader / "BepInEx" / "core", full_root / "BepInEx" / "core", loader / "BepInEx" / "core")
        target = full_root / "BepInEx" / "plugins" / "cerveth.mortal.helper"
        target.mkdir(parents=True)
        shutil.copy2(plugin, target / "MortalHelper.dll")
        (full_root / "使用说明.txt").write_text(USAGE.format(version=args.version), encoding="utf-8")
        (full_root / "第三方组件说明.txt").write_text(NOTICE, encoding="utf-8")
        make_sums(full_root)
        write_zip(full_root, out / ("%s-%s-full.zip" % (NAME, args.version)))
        made.append(full_root)

    for folder in made:
        files = [p for p in folder.rglob("*") if p.is_file()]
        total = sum(p.stat().st_size for p in files)
        print("%-46s %3d files  %6.1f MB" % (folder.name, len(files), total / (1 << 20)))
    for archive in sorted(out.glob("*.zip")):
        if archive.stem.startswith(NAME):
            print("%-46s          %6.1f MB" % (archive.name, archive.stat().st_size / (1 << 20)))
    return 0


def make_sums(folder: Path) -> None:
    lines = []
    for item in sorted(folder.rglob("*")):
        if item.is_file() and item.name != "SHA256SUMS.txt":
            lines.append("%s  %s" % (sha256(item), item.relative_to(folder).as_posix()))
    (folder / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())