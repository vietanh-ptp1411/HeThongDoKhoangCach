from pathlib import Path
root=Path(__file__).resolve().parent.parent
payload=root/'artifacts/publish/1.2.1/win-x64'
files=sorted(p for p in payload.rglob('*') if p.is_file() and p.relative_to(payload).parts[0]!='Data' and p.name!='appsettings.json')
def nsis(s):return str(s).replace('$','$$').replace('"','$\\"')
lines=['!macro InstallPayload']
for p in files:
    parent=str(p.relative_to(payload).parent)
    lines += [f'SetOutPath "$INSTDIR\\{nsis(parent)}"',f'File "{nsis(p)}"']
lines += ['!macroend','!macro UninstallPayload']
for p in files:lines.append(f'Delete "$INSTDIR\\{nsis(p.relative_to(payload))}"')
folders={p.parent for p in files}
for p in list(folders):
    folders.update(a for a in p.parents if a.is_relative_to(payload))
for p in sorted(folders,key=lambda p:len(p.parts),reverse=True):
    if p!=payload:lines.append(f'RMDir "$INSTDIR\\{nsis(p.relative_to(payload))}"')
lines.append('!macroend')
out=root/'artifacts/build/InstallerPayload.nsh'
out.write_text('\n'.join(lines),encoding='utf-8-sig')
print(out)
