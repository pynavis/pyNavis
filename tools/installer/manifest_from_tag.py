"""Writes the shipped-files manifest of a past release from its git tag.

The installer carries one manifest per release (tools/installer/manifests/<version>.txt)
so its upgrade guard (pynavis protect-extension) can tell the files a release put in
pyNavis.extension from files a user added or edited, whichever release is installed.
package.ps1 writes the manifest of the release it builds, from the exact staged bytes;
this backfills releases made before manifests existed, from a tag.

A file's line endings change between a tag, a checkout and an install (one field file
had an LF line and a CRLF line), so the hash is the one ShippedFiles.ContentHash makes:
text with every CRLF read as LF, a binary file (any NUL byte) as it is.

    python tools/installer/manifest_from_tag.py <repo> <tag> <version>

Dev-time CPython, standard library only.
"""

from __future__ import annotations

import hashlib
import os
import subprocess
import sys

EXTENSION = 'extensions/pyNavis.extension/'
HERE = os.path.dirname(os.path.abspath(__file__))


def git(repo: str, *args: str) -> bytes:
    return subprocess.run(['git', '-C', repo, *args], check=True, capture_output=True).stdout


def content_hash(data: bytes) -> str:
    """ShippedFiles.ContentHash: text with CRLF read as LF, binary as it is."""
    if b'\0' not in data:
        data = data.replace(b'\r\n', b'\n')
    return hashlib.sha256(data).hexdigest()


def main(repo: str, tag: str, version: str) -> None:
    names = git(repo, 'ls-tree', '-r', '--name-only', tag, '--', EXTENSION).decode('utf-8').splitlines()
    lines = ['# pyNavis %s, pyNavis.extension as tagged %s' % (version, tag)]
    for name in sorted(names, key=str.lower):
        rel = name[len(EXTENSION):]
        if '__pycache__/' in rel or rel.endswith('.pyc'):
            continue
        lines.append('%s  %s' % (content_hash(git(repo, 'show', '%s:%s' % (tag, name))), rel))
    out = os.path.join(HERE, 'manifests', version + '.txt')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    print('wrote %s: %d file(s)' % (out, len(names)))


if __name__ == '__main__':
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
