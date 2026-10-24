"""Packaging boundary checks; synthetic DLL only, no game files required."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.plugin = self.folder / 'MortalHelper.dll'
        self.plugin.write_bytes(b'synthetic test fixture; not a real assembly')
        self.manifest = {
            'plugin_sha256': digest(self.plugin), 'diagnostics': False,
            'contract_checks': 1, 'ally_checks': 1,
            'build_script_sha256': digest(ROOT / 'build.py'),
            'sources': {str(p.relative_to(ROOT)): digest(p) for p in (ROOT / 'src').rglob('*.cs')},
            'tests': {str(p.relative_to(ROOT)): digest(p) for p in (ROOT / 'tests').glob('*.cs')},
            'python_tests': {str(p.relative_to(ROOT)): digest(p) for p in (ROOT / 'tests').glob('test_*.py')},
        }

    def package(self, version='1.0.0'):
        (self.folder / 'build-manifest.json').write_text(json.dumps(self.manifest), encoding='utf-8')
        return subprocess.run([sys.executable, str(ROOT / 'tools/package.py'), '--plugin', str(self.plugin),
                               '--out', str(self.folder / 'dist'), '--version', version],
                              text=True, capture_output=True)

    def test_version_traversal_rejected_without_touching_victim(self):
        victim = self.folder / 'victim'
        victim.mkdir()
        marker = victim / 'keep.txt'
        marker.write_text('keep', encoding='utf-8')
        result = self.package('a/../../victim')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('version must be', result.stderr)
        self.assertEqual(marker.read_text(), 'keep')
        self.assertFalse((self.folder / 'dist').exists())

    def test_stale_source_manifest_rejected(self):
        self.manifest['sources']['src/Game/Plugin.cs'] = 'stale'
        result = self.package()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('stale', result.stderr)

    def test_version_must_match_plugin(self):
        result = self.package('99.99.99')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('version does not match', result.stderr)

    def test_package_has_checksums_and_cannot_overwrite(self):
        result = self.package()
        self.assertEqual(result.returncode, 0, result.stderr)
        archive = self.folder / 'dist/mortal-helper-1.0.0-plugin.zip'
        before = digest(archive)
        with zipfile.ZipFile(archive) as bundle:
            for line in bundle.read('SHA256SUMS.txt').decode().splitlines():
                expected, name = line.split('  ', 1)
                self.assertEqual(hashlib.sha256(bundle.read(name)).hexdigest(), expected)
            self.assertEqual([n for n in bundle.namelist() if n.endswith('.dll')],
                             ['BepInEx/plugins/cerveth.mortal.helper/MortalHelper.dll'])
            self.assertEqual(bundle.read('OPENCC-APACHE-2.0.txt'),
                             (ROOT / 'OPENCC-APACHE-2.0.txt').read_bytes())
            self.assertNotIn('PROVENANCE.md', bundle.namelist())
        self.assertNotEqual(self.package().returncode, 0)
        self.assertEqual(digest(archive), before)


if __name__ == '__main__':
    unittest.main()
