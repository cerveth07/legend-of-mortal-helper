"""Build against a user's local game and loader. Never installs or downloads files."""
import argparse
import json
import pathlib
import subprocess
import hashlib

ROOT = pathlib.Path(__file__).resolve().parent


def run_compiler(compiler, arguments, response):
    response.write_text('\n'.join('"' + str(arg) + '"' for arg in arguments), encoding='utf-8')
    subprocess.run(['dotnet', str(compiler), '-noconfig', '@' + str(response)], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', required=True, type=pathlib.Path)
    parser.add_argument('--loader', required=True, type=pathlib.Path, help='BepInEx/core folder')
    parser.add_argument('--compiler', required=True, type=pathlib.Path, help='Roslyn csc.dll')
    parser.add_argument('--test', action='store_true')
    args = parser.parse_args()
    managed = args.game.resolve() / 'Mortal_Data' / 'Managed'
    compiler = args.compiler.resolve()
    loader = args.loader.resolve()
    for path in [managed / 'Mortal.Core.dll', loader / 'BepInEx.Unity.Mono.dll', compiler]:
        if not path.is_file():
            parser.error('Required local dependency missing: ' + str(path))
    output = ROOT / 'artifacts'
    output.mkdir(exist_ok=True)
    references = {p.name: p for p in managed.glob('*.dll')}
    references.update({p.name: p for p in loader.glob('*.dll')})
    for name in ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll']:
        references[name] = managed / name
    base = ['-nologo', '-nostdlib+', '-langversion:latest', '-deterministic+', '-optimize+', '-warnaserror+']
    plugin = output / 'MortalHelper.dll'
    sources = sorted((ROOT / 'src').rglob('*.cs'))
    run_compiler(compiler, base + ['-target:library', '-out:' + str(plugin)] +
                 ['-r:' + str(p) for p in references.values()] + sources, output / 'plugin.rsp')
    manifest = {'plugin_sha256': hashlib.sha256(plugin.read_bytes()).hexdigest(),
                'sources': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},
                'references': {name: hashlib.sha256(p.read_bytes()).hexdigest() for name, p in references.items()}}
    (output / 'build-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    if args.test:
        framework = pathlib.Path(r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319')
        pure = sorted((ROOT / 'src' / 'Core').glob('*.cs')) + sorted((ROOT / 'src' / 'Recovery').glob('*.cs'))
        test_exe = output / 'ContractTests.exe'
        run_compiler(compiler, base + ['-target:exe', '-out:' + str(test_exe)] +
                     ['-r:' + str(framework / n) for n in ['mscorlib.dll', 'System.dll', 'System.Core.dll']] +
                     pure + [ROOT / 'tests' / 'Contracts.cs'], output / 'tests.rsp')
        subprocess.run([str(test_exe)], check=True, cwd=ROOT)
    print('Build completed. No files were installed in the game.')


if __name__ == '__main__':
    main()
