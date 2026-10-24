"""Build using local game/loader references, or run the BCL-only contract tests."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def native_path(path, windows):
    path = str(Path(path).resolve())
    if windows and os.name != 'nt':
        return subprocess.check_output(['wslpath', '-w', path], text=True).strip()
    return path


def run_compiler(dotnet, compiler, arguments, response):
    windows = dotnet.lower().endswith('.exe')
    converted = []
    for arg in arguments:
        arg = str(arg)
        prefix = next((p for p in ('-out:', '-r:') if arg.startswith(p)), '')
        if prefix:
            arg = prefix + native_path(arg[len(prefix):], windows)
        elif not arg.startswith('-'):
            arg = native_path(arg, windows)
        converted.append('"' + arg + '"')
    response.write_text('\n'.join(converted), encoding='utf-8')
    subprocess.run([dotnet, native_path(compiler, windows), '-noconfig',
                    '@' + native_path(response, windows)], check=True)


def run_suite(dotnet, compiler, base, framework, exe_name, response_name, sources, project, pattern, label):
    """Compile (when a compiler is given) and run one offline suite.
    Returns its positive check count; any non-zero exit or missing PASS line fails."""
    if compiler:
        output = ROOT / 'artifacts'
        exe = output / exe_name
        exe.unlink(missing_ok=True)
        run_compiler(dotnet, compiler, base + ['-target:exe', '-out:' + str(exe)] +
                     ['-r:' + str(framework / n) for n in ['mscorlib.dll', 'System.dll', 'System.Core.dll']] +
                     sources, output / response_name)
        if os.name != 'nt' and dotnet.lower().endswith('.exe'):
            exe.chmod(exe.stat().st_mode | 0o111)
        command = [str(exe)] if os.name == 'nt' or dotnet.lower().endswith('.exe') else ['mono', str(exe)]
    else:
        command = [dotnet, 'run', '--project', native_path(project, dotnet.lower().endswith('.exe')),
                   '--configuration', 'Release']
    result = subprocess.run(command, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            errors='replace')
    print(result.stdout, end='')
    match = re.search(pattern, result.stdout, re.M)
    if result.returncode or not match:
        raise SystemExit(label + ' failed or did not report a positive PASS count.')
    return int(match.group(1))


def run_tests(dotnet, compiler=None, base=None, framework=None):
    pure = sorted((ROOT / 'src' / 'Core').glob('*.cs')) + sorted((ROOT / 'src' / 'Recovery').glob('*.cs')) + [
        ROOT / 'src' / 'Game' / name for name in
        ['UiLanguage.cs', 'AnytimeStoryCodec.cs', 'AnytimeDuelJournal.cs', 'DuelSaveMenuState.cs', 'AnytimeDuelStart.cs', 'UiText.cs']]
    suites = [ROOT / 'tests' / name for name in
              ['Contracts.cs', 'TitleContracts.cs', 'NumericEditContracts.cs', 'StoryContracts.cs',
               'DuelJournalTests.cs', 'DuelSaveMenuStateTests.cs']]
    return run_suite(dotnet, compiler, base, framework, 'ContractTests.exe', 'tests.rsp', pure + suites,
                     ROOT / 'tests' / 'Contracts.csproj',
                     r'^PASS: ([1-9][0-9]*) contract checks(?:[;.]|\s*$)', 'Contract tests')


def run_ally_tests(dotnet, compiler=None, base=None, framework=None):
    """Group-battle ally-health routing suite; a separate binary because it stubs the
    Harmony and game types that the contract suite compiles for real."""
    return run_suite(dotnet, compiler, base, framework, 'GroupAllyTests.exe', 'group-ally.rsp',
                     [ROOT / 'src' / 'Game' / 'GroupBattleTools.cs',
                      ROOT / 'tests' / 'GroupAllyHealthBehavior.cs'],
                     ROOT / 'tests' / 'GroupAllyHealth.csproj',
                     r'^PASS: ([1-9][0-9]*) production ally-health routing checks', 'Group ally-health checks')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path)
    parser.add_argument('--loader', type=Path, help='BepInEx/core folder')
    parser.add_argument('--compiler', type=Path, help='Roslyn csc.dll')
    parser.add_argument('--dotnet', default=shutil.which('dotnet') or shutil.which('dotnet.exe') or 'dotnet')
    parser.add_argument('--framework', type=Path, help='local .NET Framework reference folder for --test')
    parser.add_argument('--test', action='store_true')
    parser.add_argument('--test-only', action='store_true', help='run .NET 8 tests without game, loader or Roslyn')
    parser.add_argument('--diagnostics', action='store_true', help='development probe; cannot be packaged')
    args = parser.parse_args()
    if args.test_only:
        run_tests(args.dotnet)
        run_ally_tests(args.dotnet)
        return
    if not all((args.game, args.loader, args.compiler)):
        parser.error('--game, --loader and --compiler are required for a plugin build')
    output = ROOT / 'artifacts'
    output.mkdir(exist_ok=True)
    plugin = output / 'MortalHelper.dll'
    manifest_path = output / 'build-manifest.json'
    # Invalidate the previous build before invoking any compiler or tests.
    plugin.unlink(missing_ok=True)
    manifest_path.unlink(missing_ok=True)
    managed = args.game.resolve() / 'Mortal_Data' / 'Managed'
    compiler = args.compiler.resolve()
    loader = args.loader.resolve()
    for path in [managed / 'Mortal.Core.dll', loader / 'BepInEx.Unity.Mono.dll', compiler]:
        if not path.is_file():
            parser.error('Required local dependency missing: ' + str(path))
    references = {p.name: p for p in managed.glob('*.dll')}
    references.update({p.name: p for p in loader.glob('*.dll')})
    for name in ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll']:
        references[name] = managed / name
    base = ['-nologo', '-nostdlib+', '-langversion:latest', '-deterministic+', '-optimize+', '-warnaserror+']
    sources = sorted((ROOT / 'src').rglob('*.cs'))
    compiled = sources if args.diagnostics else [p for p in sources if p.name != 'Probe.cs']
    defines = [] if args.diagnostics else ['-define:NO_PROBE']
    run_compiler(args.dotnet, compiler, base + defines + ['-target:library', '-out:' + str(plugin)] +
                 ['-r:' + str(p) for p in references.values()] + compiled, output / 'plugin.rsp')
    count = None
    ally_count = None
    if args.test:
        framework = args.framework or Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319' if os.name == 'nt' else '/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319')
        count = run_tests(args.dotnet, compiler, base, framework)
        ally_count = run_ally_tests(args.dotnet, compiler, base, framework)
    # No 'references' hash table: nothing ever read it. tools/package.py used to strip
    # it from the shipped copy, which only hid the fact that the hashes had no consumer.
    manifest = {'plugin_sha256': sha256(plugin), 'diagnostics': args.diagnostics, 'contract_checks': count,
                'ally_checks': ally_count,
                'sources': {str(p.relative_to(ROOT)): sha256(p) for p in sources},
                'build_script_sha256': sha256(Path(__file__)),
                'tests': {str(p.relative_to(ROOT)): sha256(p) for p in sorted((ROOT / 'tests').glob('*.cs'))},
                'python_tests': {str(p.relative_to(ROOT)): sha256(p) for p in sorted((ROOT / 'tests').glob('test_*.py'))}}
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print('Build completed. No files were installed in the game.')


if __name__ == '__main__':
    main()
