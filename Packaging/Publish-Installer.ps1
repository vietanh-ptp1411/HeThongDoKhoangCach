param(
    [string]$CompilerPath = '',
    [string]$GuideDirectory = '',
    [string]$ReleaseDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$CompilerPath) { $CompilerPath = Join-Path $projectRoot 'artifacts\tools\nsis-3.13\makensis.exe' }
if (!$GuideDirectory) { $GuideDirectory = Join-Path $projectRoot 'artifacts\release\1.2.1' }
if (!$ReleaseDirectory) { $ReleaseDirectory = Join-Path $projectRoot 'artifacts\release\1.2.1' }
foreach ($requiredFile in @($CompilerPath, (Join-Path $GuideDirectory 'HuongDanSuDung.pdf'), (Join-Path $GuideDirectory 'HuongDanSuDung.docx'))) {
    if (!(Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Missing release input: $requiredFile" }
}
$publishFolder = Join-Path $projectRoot 'artifacts\publish\BeltTensionMeasurement\1.2.1\win-x64'
& dotnet publish (Join-Path $projectRoot 'BeltTensionMeasurement.csproj') -c Release -r win-x64 --self-contained true --artifacts-path (Join-Path $projectRoot 'artifacts\build') -o $publishFolder -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
& python (Join-Path $PSScriptRoot 'Collect-Licenses.py')
if ($LASTEXITCODE -ne 0) { throw 'Collecting component notices failed.' }
if ((Test-Path (Join-Path $publishFolder 'appsettings.json')) -or (Test-Path (Join-Path $publishFolder 'Data\results.jsonl')) -or (Test-Path (Join-Path $publishFolder 'Data\HeThongDo.db'))) {
    throw 'Publish folder contains runtime user data. Use a clean publish folder before packaging.'
}
& python (Join-Path $PSScriptRoot 'Generate-Payload.py')
if ($LASTEXITCODE -ne 0) { throw 'Generating installer manifest failed.' }
& $CompilerPath /V2 /INPUTCHARSET UTF8 "/DPayloadDir=$publishFolder" "/DDocumentsDir=$GuideDirectory" "/DReleaseDir=$ReleaseDirectory" "/DPayloadInclude=$(Join-Path $projectRoot 'artifacts\build\InstallerPayload.nsh')" (Join-Path $PSScriptRoot 'BeltMeasurement.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Get-ChildItem -LiteralPath $ReleaseDirectory -File | Where-Object Extension -In '.exe','.pdf','.docx' | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { '{0}  {1}' -f $_.Hash, [IO.Path]::GetFileName($_.Path) } |
    Set-Content -LiteralPath (Join-Path $ReleaseDirectory 'SHA256SUMS.txt') -Encoding ascii
