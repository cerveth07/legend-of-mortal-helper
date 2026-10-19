"""Check the candidate source inventory. This is not a legal certification."""
import pathlib
import re
import subprocess

root = pathlib.Path(__file__).resolve().parents[1]
files = [p for p in root.rglob('*') if p.is_file() and not any(
    part in {'.git', 'artifacts', '.local', '__pycache__'} for part in p.relative_to(root).parts)]
errors = []
for path in files:
    if path.suffix.lower() in {'.dll', '.exe', '.zip', '.lua', '.dat', '.loms', '.duel', '.asset', '.bundle'}:
        errors.append('Unexpected binary or game data: ' + str(path.relative_to(root)))
    if path.suffix == '.cs':
        source = path.read_text(encoding='utf-8')
        if re.search(r'\bBinaryFormatter\b|\b(?:using|namespace)\s+(?:LOMSave2|DiceMaster)\b', source):
            errors.append('Legacy implementation dependency: ' + str(path.relative_to(root)))
if (root / '.git').is_dir():
    result = subprocess.run(['git', 'rev-list', '--max-parents=0', 'HEAD'], cwd=root, text=True, capture_output=True)
    if result.returncode == 0 and len(result.stdout.splitlines()) != 1:
        errors.append('Expected exactly one root commit in the new repository.')
if errors:
    raise SystemExit('\n'.join(errors))
print(f'PASS: {len(files)} source/document files; no bundled game/legacy binaries or old namespaces.')
print('This inventory check does not establish copyright clearance or gameplay correctness.')
