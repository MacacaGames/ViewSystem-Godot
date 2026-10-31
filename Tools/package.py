#!/usr/bin/env python3
"""Create a deterministic installable addon ZIP from a checkout or submodule."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

source = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
version = json.loads((source / 'module.json').read_text())['version']
args.output.mkdir(parents=True, exist_ok=True)
target = args.output / f'macaca-viewsystem-{version}.zip'
excluded = {'.git', '.github', '.godot', '.tools', '__pycache__', 'bin', 'obj', 'artifacts'}
files = {}
for path in sorted(source.rglob('*')):
    relative = path.relative_to(source)
    if not path.is_file() or any(part in excluded for part in relative.parts):
        continue
    if path.is_symlink():
        raise ValueError(f'Unexpected symlink in addon: {relative}')
    if relative.name in {'.DS_Store', '.gitignore', '.gitattributes'}:
        continue
    files['addons/macaca_viewsystem/' + relative.as_posix()] = path.read_bytes()
manifest = {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}
files['macaca-viewsystem-manifest.json'] = (json.dumps({'version': version, 'files': manifest}, indent=2) + '\n').encode()
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as archive:
    for name, data in files.items():
        info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o100644 << 16
        archive.writestr(info, data)
print(json.dumps({'artifact': str(target), 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(), 'files': len(manifest)}, indent=2))
