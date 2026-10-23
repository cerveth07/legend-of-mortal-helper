"""Check the candidate source inventory. This is not a legal certification."""
import pathlib
import re
import json
import hashlib
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
files = [p for p in root.rglob('*') if p.is_file() and not any(
    part in {'.git', 'artifacts', 'dist', '.local', '__pycache__'} for part in p.relative_to(root).parts)]
errors = []
# Only exact, documented upstream corresponding-source archives are allowed.
# Loader/game binaries remain excluded from the public source tree.
manifest = root / 'installer' / 'third-party' / 'manifest.json'
source_archives = {}
if manifest.is_file():
    for item in json.loads(manifest.read_text(encoding='utf-8')).get('sources', []):
        source_archives[manifest.parent / item['path']] = item['sha256']
for path in files:
    if path in source_archives:
        if hashlib.sha256(path.read_bytes()).hexdigest() != source_archives[path]:
            errors.append('Corresponding source hash mismatch: ' + str(path.relative_to(root)))
        with zipfile.ZipFile(path) as source:
            if any(name.lower().endswith(('.dll', '.exe', '.dat', '.bundle', '.asset')) for name in source.namelist()):
                errors.append('Binary in corresponding source archive: ' + str(path.relative_to(root)))
        continue
    if path.suffix.lower() in {'.dll', '.exe', '.zip', '.lua', '.dat', '.loms', '.duel', '.asset', '.bundle'}:
        errors.append('Unexpected binary or game data: ' + str(path.relative_to(root)))
    if path.suffix == '.cs':
        source = path.read_text(encoding='utf-8')
        # The game itself uses BinaryFormatter for its native .dat files; its
        # presence is not evidence of a dependency on the old mod's source.
        if re.search(r'\b(?:using|namespace)\s+(?:LOMSave2|DiceMaster)\b', source):
            errors.append('Legacy implementation dependency: ' + str(path.relative_to(root)))
if errors:
    raise SystemExit('\n'.join(errors))
print(f'PASS: {len(files)} source/document files; no bundled game/legacy binaries or old namespaces.')
print('This inventory check does not establish copyright clearance or gameplay correctness.')
