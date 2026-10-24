"""Build small offline Windows installer; Windows .NET Framework 4.7.2+ required."""
import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PREFIX = 'BepInEx/plugins/cerveth.mortal.helper/'


def windows(path):
    return subprocess.check_output(['wslpath', '-w', str(path)], text=True).strip() if os.name != 'nt' else str(path)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--plugin', required=True, type=Path)
    ap.add_argument('--loader-zip', required=True, type=Path)
    ap.add_argument('--out', type=Path, default=ROOT / 'dist' / 'MortalHelper-Setup.exe')
    ap.add_argument('--compiler', type=Path, default=Path('/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'))
    ap.add_argument('--test', action='store_true')
    args = ap.parse_args()
    args.plugin = args.plugin.resolve()
    args.loader_zip = args.loader_zip.resolve()
    args.out = args.out.resolve()
    if args.out.exists():
        ap.error('output exists; use a fresh --out path')
    manifest_path = ROOT / 'installer' / 'third-party' / 'manifest.json'
    if not manifest_path.is_file():
        ap.error('loader redistribution manifest missing; see installer/README.md')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    if manifest.get('redistribution_verified') is not True or manifest.get('archive_sha256') != digest(args.loader_zip):
        ap.error('loader archive is unverified or differs from approved distribution manifest')
    # The notices shipped in the payload claim both corresponding-source archives are
    # inside it, so an empty (or truncated) list must fail here, not be skipped.
    sources = manifest.get('sources') or []
    if len(sources) < 2:
        ap.error('manifest must list both corresponding-source archives (BepInEx and UnityDoorstop)')
    for source in sources:
        source_path = manifest_path.parent / source['path']
        if not source_path.is_file() or digest(source_path) != source['sha256']:
            ap.error('required corresponding source archive is missing or changed: '+source['path'])
    loader_files = manifest.get('loader_files') or {}
    if not loader_files:
        ap.error('manifest declares no loader file hashes to verify')
    with zipfile.ZipFile(args.loader_zip) as archive:
        packed = {item.filename: item for item in archive.infolist() if not item.is_dir()}
        for name, expected in loader_files.items():
            if name not in packed:
                ap.error('loader archive is missing a file the manifest lists: '+name)
            if hashlib.sha256(archive.read(packed[name])).hexdigest() != expected:
                ap.error('loader file hash differs from the manifest: '+name)
    with tempfile.TemporaryDirectory(prefix='mortal-installer-') as folder:
        temp = Path(folder)
        # Reuse package.py validation: release plugin must match tested sources and manifest.
        subprocess.run([sys.executable, str(ROOT/'tools/package.py'), '--plugin', str(args.plugin), '--out', str(temp/'package')], check=True)
        plugin_zip = next((temp/'package').glob('*.zip'))
        payload = temp/'payload.zip'
        with zipfile.ZipFile(payload,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as target:
            with zipfile.ZipFile(args.loader_zip) as loader:
                for item in loader.infolist():
                    if item.is_dir() or item.filename.endswith('.xml') or item.filename == 'changelog.txt':
                        continue
                    if item.filename.startswith('/') or '..' in Path(item.filename).parts:
                        raise ValueError('unsafe loader archive path')
                    target.writestr(item.filename,loader.read(item))
            with zipfile.ZipFile(plugin_zip) as plugin:
                for item in plugin.infolist():
                    if item.is_dir():
                        continue
                    name = item.filename if item.filename.startswith(PREFIX) else PREFIX+'docs/'+item.filename
                    target.writestr(name,plugin.read(item))
            for item in sorted(manifest_path.parent.rglob('*')):
                if item.is_file():
                    target.write(item,PREFIX+'third-party/'+item.relative_to(manifest_path.parent).as_posix())
        args.out.parent.mkdir(parents=True, exist_ok=True)
        common=[str(args.compiler),'/nologo','/optimize+','/platform:anycpu','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll']
        source=windows(ROOT/'installer/Installer.cs')
        if args.test:
            tests=temp/'InstallerTests.exe'
            subprocess.run(common+['/target:exe','/main:BackendTests','/out:'+windows(tests),source,windows(ROOT/'installer/BackendTests.cs')],check=True)
            if os.name!='nt': tests.chmod(0o755)
            subprocess.run([str(tests),windows(args.loader_zip)],check=True)
        subprocess.run(common+['/target:winexe','/win32manifest:'+windows(ROOT/'installer/app.manifest'),'/out:'+windows(args.out),'/resource:'+windows(payload)+',payload.zip',source],check=True)
    args.out.with_suffix('.exe.sha256').write_text(digest(args.out)+'  '+args.out.name+'\n',encoding='utf-8')
    print('%s (%0.2f MiB) SHA256 %s'%(args.out,args.out.stat().st_size/(1<<20),digest(args.out)))
    return 0

if __name__=='__main__':
    raise SystemExit(main())
