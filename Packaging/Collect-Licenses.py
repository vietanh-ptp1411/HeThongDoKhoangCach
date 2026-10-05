from pathlib import Path
import json
import shutil
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
assets = json.loads((root/'artifacts/build/obj/BeltTensionMeasurement/project.assets.json').read_text(encoding='utf-8'))
payload = root/'artifacts/publish/BeltTensionMeasurement/1.2.1/win-x64'
destination = payload/'Licenses'
destination.mkdir(exist_ok=True)
cache = Path(next(iter(assets['packageFolders'])))
lines = ['THIRD-PARTY COMPONENTS / Belt Measurement 1.2.1', '',
         'Original package metadata and available license/notice files are retained in Licenses/.',
         'Each component remains subject to its own license.', '']
packages = dict(assets['libraries'])
runtime = json.loads((payload/'BeltTensionMeasurement.runtimeconfig.json').read_text())
for framework in runtime['runtimeOptions']['includedFrameworks']:
    identifier = framework['name'] + '.Runtime.win-x64'
    packages[f"{identifier}/{framework['version']}"] = {'type':'package','path':f"{identifier.lower()}/{framework['version']}"}

for identity, info in sorted(packages.items()):
    if info.get('type') != 'package': continue
    folder = cache/info['path']
    target = destination/identity.replace('/', '-')
    target.mkdir(exist_ok=True)
    nuspec = next(folder.glob('*.nuspec'))
    shutil.copy2(nuspec, target/nuspec.name)
    tree = ET.parse(nuspec)
    metadata = next(n for n in tree.getroot() if n.tag.endswith('metadata'))
    fields = {n.tag.split('}')[-1]:n.text or '' for n in metadata}
    lines.append(f"{identity}: {fields.get('license') or fields.get('licenseUrl') or 'See original package metadata'}")
    if fields.get('copyright'): lines.append(fields['copyright'])
    lines.append(f"https://www.nuget.org/packages/{identity}")
    for file in folder.rglob('*'):
        if file.is_file() and any(word in file.name.lower() for word in ('license','licence','notice','copying')):
            relative = file.relative_to(folder)
            out = target/relative
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file,out)
    lines.append('')
(payload/'THIRD-PARTY-NOTICES.txt').write_text('\n'.join(lines),encoding='utf-8')
print(f'Collected {len(list(destination.iterdir()))} component notices.')
