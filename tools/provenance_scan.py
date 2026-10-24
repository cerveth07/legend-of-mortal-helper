"""Measure textual overlap between this project and a reference tree.

This is a review aid, not a legal conclusion and not a similarity guarantee.
It strips comments, tokenises identifiers and punctuation, and reports the
longest shared token runs plus per-file shingle containment. Boilerplate such
as `using`, braces and braces-only lines contributes shingles, so read the
sample runs before drawing conclusions.
"""
import argparse
import pathlib
import re

TOKEN = re.compile(r'[A-Za-z_][A-Za-z_0-9]*|[0-9]+|\S')
COMMENT = re.compile(r'//[^\n]*|/\*.*?\*/', re.S)


def tokens(path):
    text = path.read_text(encoding='utf-8', errors='replace')
    text = COMMENT.sub(' ', text)
    return TOKEN.findall(text)


def shingles(items, size):
    return {tuple(items[i:i + size]) for i in range(len(items) - size + 1)}


def longest_run(left, right):
    """Longest common contiguous token run between two token lists."""
    best, end = 0, 0
    previous = [0] * (len(right) + 1)
    for i in range(1, len(left) + 1):
        current = [0] * (len(right) + 1)
        for j in range(1, len(right) + 1):
            if left[i - 1] == right[j - 1]:
                current[j] = previous[j - 1] + 1
                if current[j] > best:
                    best, end = current[j], i
        previous = current
    return best, ' '.join(left[end - best:end])


def files(root, pattern):
    return sorted(p for p in root.rglob(pattern) if p.is_file())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--new', required=True, type=pathlib.Path)
    parser.add_argument('--old', required=True, type=pathlib.Path)
    parser.add_argument('--size', type=int, default=8, help='shingle length in tokens')
    args = parser.parse_args()
    old = [(p, tokens(p)) for p in files(args.old, '*.cs')]
    old_shingles = [(p, shingles(t, args.size)) for p, t in old]
    worst = 0.0
    for path in files(args.new, '*.cs'):
        mine = tokens(path)
        mine_shingles = shingles(mine, args.size)
        if not mine_shingles:
            continue
        matches = []
        for (other, theirs) in old_shingles:
            shared = len(mine_shingles & theirs)
            if shared:
                matches.append((shared / len(mine_shingles), other, shared))
        matches.sort(reverse=True)
        best_run, sample = 0, ''
        for other, their_tokens in old:
            run, text = longest_run(mine, their_tokens)
            if run > best_run:
                best_run, sample = run, text
        share = matches[0][0] if matches else 0.0
        worst = max(worst, share)
        detail = ', '.join('{0} {1:.1%}'.format(p.relative_to(args.old), s) for s, p, _ in matches[:2])
        print('{0:<44} {1:>6.1%} shingles  run {2:>4}  {3}'.format(
            str(path.relative_to(args.new)), share, best_run, detail))
        if sample and share > 0.02:
            print('    longest shared run: ' + sample[:120])
    print('worst per-file shingle containment: {0:.1%}'.format(worst))
    print('Heuristic only: shared boilerplate, game API names and common idioms '
          'appear in any C# project. Absence of overlap is not proof of independence.')


if __name__ == '__main__':
    main()
