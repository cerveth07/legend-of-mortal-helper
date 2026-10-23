"""Package a tested current plugin without redistributing game or loader binaries."""

import argparse
import hashlib
import json
import re
import shutil
import zipfile
from pathlib import Path

VERSION = "0.1.1"
ROOT = Path(__file__).resolve().parents[1]
NAME = "mortal-helper"  # ASCII on purpose: the zip name ends up in URLs

USAGE = """活侠·万象录 {version} —— 使用说明
================================================

这是《活侠传》的辅助插件，仅插件包需要已安装 BepInEx 6 Mono x64。
完全退出游戏并备份存档，然后将 BepInEx/plugins/cerveth.mortal.helper/
复制到游戏目录的同名位置。从 Steam 启动游戏，按 Tab 打开面板。
版本：{version}。检查 BepInEx/LogOutput.log 中的插件初始化消息。
卸载时只移走本插件 MortalHelper.dll，共用加载器及其他插件请保留。
关闭或移走插件不会撤销已经应用并保存的属性修改。

注意事项
--------
- 如果游戏目录里已有 BepInEx（其他 Mod 自带），不要直接覆盖：先确认版本，
  或改用「仅插件」包，只放 MortalHelper.dll。
- 启动时若检测到 MortalFateHelper.dll、LOMSave2.dll、DiceMaster*.dll、
  ControlMyDestiny.dll 等旧插件，本插件会自行停用并在日志里报冲突，
  请先把它们移出 plugins 目录。
- 不要在游戏运行时替换文件。
- 数值编辑支持商店/菜单、剧情稳定等待和单挑行动等待；加载/恢复/行动结算时暂缓。
  商店修改物品会重置待购/待售数量；剧情商店修改后请进入下一段剧情再存中途档。
- 「养成辅助→称号」可选择称号外观；已获记录和选择随各自存档保存，新开游戏不继承。
  旧档只能确认当前称号；新增称号功能由用户实机验收。选择后请正常存档。
- 通关故事书脊保留原生12格，存满每次追加6格，默认上限30；支持横向拖动及滚轮。
  可在「存档设置」调整上限；新增故事栏位待用户实机验收。
- 「地图」页可查看崆峒派原图，滚轮缩放、按住拖动，点击「复位」恢复全图。
- 控骰条可按住「基础点数」或空白处拖动，松手自动记住位置。
- 「存档设置→随时存档」为默认关闭的实验功能；目前只支持稳定剧情对话、
  对话选项、骰子掷骰前/结果页与单挑行动点；骰子页点击轮盘旁「存档」。
  掷骰动画期间不开放保存；实机验收范围见包内「随时存档验收.md」。
- 中途档依赖同槽 .dat 旁的 .mha（剧情）或 .mhduel（单挑）。复制存档时
  请连同附加文件复制；卸载插件前先改用普通原生存档。
"""



def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def write_zip(folder: Path, archive: Path) -> None:
    with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
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

    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?", args.version):
        parser.error("version must be a semantic version without paths")
    if args.loader:
        parser.error("Full loader bundles are disabled until per-component licenses and source-supply notices are verified; use the plugin-only package.")
    plugin = Path(args.plugin).resolve()
    if not plugin.is_file():
        parser.error("plugin not found: %s" % plugin)
    manifest_path = plugin.parent / "build-manifest.json"
    if not manifest_path.is_file():
        parser.error("build manifest missing; run build.py --test first")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    sources = {str(p.relative_to(ROOT)): sha256(p) for p in sorted((ROOT / "src").rglob("*.cs"))}
    tests = {str(p.relative_to(ROOT)): sha256(p) for p in sorted((ROOT / "tests").glob("*.cs"))}
    python_tests = {str(p.relative_to(ROOT)): sha256(p) for p in sorted((ROOT / "tests").glob("test_*.py"))}
    if (manifest.get("plugin_sha256") != sha256(plugin) or manifest.get("sources") != sources
            or manifest.get("tests") != tests or manifest.get("python_tests") != python_tests
            or manifest.get("build_script_sha256") != sha256(ROOT / "build.py")
            or manifest.get("diagnostics") is not False
            or not isinstance(manifest.get("contract_checks"), int) or manifest["contract_checks"] <= 0
            or not isinstance(manifest.get("ally_checks"), int) or manifest["ally_checks"] <= 0):
        parser.error("build is stale, untested or diagnostic; rebuild current sources with build.py --test")
    version_source = (ROOT / "src" / "Game" / "Plugin.cs").read_text(encoding="utf-8")
    if not re.search(r'\[BepInPlugin\("cerveth\.mortal\.helper",\s*"[^"\n]+",\s*"' + re.escape(args.version) + r'"\)\]', version_source):
        parser.error("package version does not match the plugin source version")
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    made = []
    plugin_root = out / ("%s-%s-plugin" % (NAME, args.version))
    if plugin_root.exists() or (out / ("%s-%s-plugin.zip" % (NAME, args.version))).exists():
        parser.error("output already exists; choose a fresh --out directory")
    (plugin_root / "BepInEx" / "plugins" / "cerveth.mortal.helper").mkdir(parents=True)
    shutil.copy2(plugin, plugin_root / "BepInEx" / "plugins" / "cerveth.mortal.helper" / "MortalHelper.dll")
    (plugin_root / "使用说明.txt").write_text(USAGE.format(version=args.version), encoding="utf-8")
    shutil.copy2(ROOT / "LICENSE", plugin_root / "LICENSE")
    shutil.copy2(ROOT / "THIRD_PARTY.md", plugin_root / "THIRD_PARTY.md")
    shutil.copy2(ROOT / "PROVENANCE.md", plugin_root / "PROVENANCE.md")
    shutil.copy2(ROOT / "docs" / "ANYTIME-SAVE-ACCEPTANCE.md", plugin_root / "随时存档验收.md")
    shutil.copy2(manifest_path, plugin_root / "build-manifest.json")
    make_sums(plugin_root)
    write_zip(plugin_root, out / ("%s-%s-plugin.zip" % (NAME, args.version)))
    made.append(plugin_root)

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
