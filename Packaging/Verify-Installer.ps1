param([string]$InstallerPath = '', [string]$PreviousInstallerPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$InstallerPath) { $InstallerPath = Join-Path $projectRoot 'artifacts\release\1.2.1\HeThongDoLucCangBelt-Setup-1.2.1-win-x64.exe' }
$testDirectory = Join-Path $projectRoot ('artifacts\qa\installed-' + [Guid]::NewGuid().ToString('N'))
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MVALabBeltMeasurement'
if (Test-Path $registryPath) { throw 'An installed copy is already registered. Do not replace it during installer QA.' }
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Hệ thống đo lực căng Belt.lnk'
$startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'Hệ thống đo lực căng Belt'
if ((Test-Path -LiteralPath $desktopShortcut) -or (Test-Path -LiteralPath $startMenuFolder)) { throw 'Existing application shortcuts found; installer QA will not overwrite them.' }
New-Item -ItemType Directory -Force (Split-Path $testDirectory -Parent) | Out-Null

function Assert-Check([bool]$Passed, [string]$Description) {
    if (!$Passed) { throw "FAIL: $Description" }
    Write-Output "PASS: $Description"
}
function Install-TestCopy([string]$PackagePath = $InstallerPath) {
    $setup = Start-Process -FilePath $PackagePath -ArgumentList @('/S', ('/D=' + $testDirectory)) -WindowStyle Hidden -Wait -PassThru
    Assert-Check ($setup.ExitCode -eq 0) 'Silent installer exits successfully'
}

Install-TestCopy
Assert-Check (Test-Path (Join-Path $testDirectory 'HeThongDoKhoangCach.exe')) 'Application executable is installed'
Assert-Check (Test-Path (Join-Path $testDirectory 'coreclr.dll')) 'Self-contained .NET runtime is installed'
Assert-Check (Test-Path (Join-Path $testDirectory 'e_sqlite3.dll')) 'Native SQLite library is installed'
Assert-Check (Test-Path (Join-Path $testDirectory 'HuongDan\HuongDanSuDung.pdf')) 'PDF guide is installed'
Assert-Check (Test-Path (Join-Path $testDirectory 'HuongDan\HuongDanSuDung.docx')) 'Editable Word guide is installed'
Assert-Check (!(Test-Path (Join-Path $testDirectory 'Data\master.csv'))) 'Fresh installation does not include a master file'
Assert-Check (Test-Path $registryPath) 'Windows uninstall entry is registered'
Assert-Check ((Test-Path -LiteralPath $desktopShortcut) -and (Test-Path -LiteralPath $startMenuFolder)) 'Desktop and Start Menu shortcuts exist'

$app = Start-Process -FilePath (Join-Path $testDirectory 'HeThongDoKhoangCach.exe') -WorkingDirectory $testDirectory -WindowStyle Hidden -PassThru
try {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $database = Join-Path $testDirectory 'Data\HeThongDo.db'
    while (!(Test-Path $database) -and !$app.HasExited -and $timer.Elapsed.TotalSeconds -lt 15) { Start-Sleep -Milliseconds 200; $app.Refresh() }
    Assert-Check (!$app.HasExited -and (Test-Path $database)) 'Installed application starts and initializes SQLite catalog'
    $configuration = Get-Content (Join-Path $testDirectory 'appsettings.json') -Raw | ConvertFrom-Json
    Assert-Check ($configuration.Plc.Protocol -eq 'Simulation') 'First launch defaults to simulation'
    Assert-Check (!($configuration.PSObject.Properties.Name -contains 'MasterFilePath')) 'New configuration contains no master file setting'
    Assert-Check ((Get-ItemProperty $registryPath).DisplayVersion -eq '1.2.1') 'Installed application is registered as version 1.2.1'
    $localRuntime = @((Get-Process -Id $app.Id).Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' -and $_.FileName.StartsWith($testDirectory, [StringComparison]::OrdinalIgnoreCase) })
    Assert-Check ($localRuntime.Count -eq 1) 'Application loads its bundled runtime, not the machine-wide runtime'
}
finally {
    if (!$app.HasExited) {
        $null = $app.CloseMainWindow()
        if (!$app.WaitForExit(5000)) { Stop-Process -Id $app.Id -Force }
    }
}

$settingsFile = Join-Path $testDirectory 'appsettings.json'
$configuration.Inspector = 'QA-PRESERVE-ON-UPGRADE'
$configuration | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsFile -Encoding UTF8
$historyFile = Join-Path $testDirectory 'Data\results.jsonl'
'{"No":1,"Serial":"QA-KEEP","Value":3.7,"Lsl":3,"Usl":4,"IsOk":true,"QrText":"3.70","InspectedAt":"2026-09-30T10:00:00"}' | Set-Content -LiteralPath $historyFile -Encoding UTF8
$masterFile = Join-Path $testDirectory 'Data\master.csv'
@('Key,OrderNo,Line,Model','QA-KEEP,QA-ORDER,QA-LINE,CPX') | Set-Content -LiteralPath $masterFile -Encoding UTF8
$userFiles = @($settingsFile,$database,$historyFile,$masterFile)
$hashesBefore = @{}
foreach ($file in $userFiles) { $hashesBefore[$file] = (Get-FileHash -LiteralPath $file).Hash }
if ($PreviousInstallerPath) {
    Install-TestCopy $PreviousInstallerPath
    Assert-Check ((Get-ItemProperty $registryPath).DisplayVersion -eq '1.2.0') 'Previous release is staged for upgrade verification'
}
Install-TestCopy
Assert-Check ((Get-ItemProperty $registryPath).DisplayVersion -eq '1.2.1') 'Upgrade registers the new version 1.2.1'
foreach ($file in $userFiles) { Assert-Check ((Get-FileHash -LiteralPath $file).Hash -eq $hashesBefore[$file]) ('Upgrade preserves ' + [IO.Path]::GetFileName($file)) }

$uninstaller = Join-Path $testDirectory 'Uninstall.exe'
# _?= runs uninstall from the original location and makes the exit code observable.
$remove = Start-Process -FilePath $uninstaller -ArgumentList @('/S', ('_?=' + $testDirectory)) -WindowStyle Hidden -Wait -PassThru
Assert-Check ($remove.ExitCode -eq 0) 'Uninstaller exits successfully'
Assert-Check (!(Test-Path (Join-Path $testDirectory 'HeThongDoKhoangCach.exe'))) 'Uninstaller removes application binaries'
Assert-Check (!(Test-Path $registryPath)) 'Uninstaller removes Windows app registration'
Assert-Check (!(Test-Path -LiteralPath $desktopShortcut) -and !(Test-Path -LiteralPath $startMenuFolder)) 'Uninstaller removes shortcuts'
foreach ($file in $userFiles) { Assert-Check ((Get-FileHash -LiteralPath $file).Hash -eq $hashesBefore[$file]) ('Uninstall preserves ' + [IO.Path]::GetFileName($file)) }
Write-Output "Installer QA data retained at: $testDirectory"
