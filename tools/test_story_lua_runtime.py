"""Run production story capture/restore methods against the real bundled MoonSharp.

Build the plugin first. This creates a separate read-only test EXE; it does not
start Unity or modify game files, saves, configuration, or the supplied DLL.
"""
import argparse
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from build import native_path, run_compiler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plugin', type=Path, required=True)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--loader', type=Path, required=True)
    parser.add_argument('--compiler', type=Path, required=True)
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--framework', type=Path, default=Path('/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319'))
    args = parser.parse_args()
    managed = args.game / 'Mortal_Data' / 'Managed'
    output = ROOT / 'artifacts' / 'story-lua-runtime'
    output.mkdir(parents=True, exist_ok=True)
    executable = output / 'StoryLuaRuntime.exe'
    references = [managed / 'Fungus.dll', managed / 'netstandard.dll'] + [
        args.framework / name for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')]
    run_compiler(args.dotnet, args.compiler, [
        '-nologo', '-define:STORY_LUA_RUNTIME_TEST', '-target:exe', '-out:' + str(executable),
    ] + ['-r:' + str(path) for path in references] + [ROOT / 'tests' / 'StoryLuaRuntime.cs'], output / 'runtime.rsp')
    executable.chmod(executable.stat().st_mode | 0o111)
    windows = args.dotnet.lower().endswith('.exe')
    result = subprocess.run([str(executable)] + [native_path(path, windows) for path in (
        args.plugin, managed, args.loader)], cwd=ROOT, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, errors='replace')
    print(result.stdout, end='')
    if result.returncode or 'PASS: 16 production Lua behavior checks' not in result.stdout:
        raise SystemExit('Production Lua runtime checks failed.')


if __name__ == '__main__':
    main()
