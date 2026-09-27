param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\WorkplaceOrchestrator'),
    [switch]$NoShortcuts,
    [switch]$NoRegistration,
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($InstallDirectory)
$source = Join-Path $PSScriptRoot 'payload'
if (-not (Test-Path -LiteralPath $source)) { $source=Join-Path $PSScriptRoot 'candidate' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'WorkplaceOrchestrator.exe'))) { throw 'Build the application first with .\build.ps1.' }
if ($destination -eq [IO.Path]::GetPathRoot($destination) -or $destination -eq $PSScriptRoot -or $destination -eq $source) { throw 'Choose a separate application installation folder.' }
if ((Test-Path -LiteralPath $destination) -and -not (Test-Path -LiteralPath (Join-Path $destination '.workplace-orchestrator-install')) -and (Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) { throw 'The destination is not empty and is not an existing Workplace Orchestrator installation.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$exe=Join-Path $destination 'WorkplaceOrchestrator.exe'
if (Get-Process -Name WorkplaceOrchestrator -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { throw 'Exit Workplace from its tray menu before updating. Running workspace applications can remain open.' }
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'WorkplaceOrchestrator.exe'))
if($version.ProductName -ne 'Workplace Orchestrator' -or $version.FileVersion -ne '1.1.0.0') { throw 'The payload is not the expected Workplace Orchestrator 1.1 release.' }
Copy-Item -LiteralPath (Join-Path $source 'WorkplaceOrchestrator.exe'),(Join-Path $source 'WorkplaceOrchestrator.exe.config'),(Join-Path $source 'WorkplaceOrchestrator.ico'),(Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $destination -Force
Set-Content -LiteralPath (Join-Path $destination '.workplace-orchestrator-install') -Value 'WorkplaceOrchestrator 1.1.0'
if (-not $NoShortcuts) {
    $register=Start-Process -FilePath $exe -ArgumentList '--register-shell' -WindowStyle Hidden -Wait -PassThru
    if($register.ExitCode -ne 0){throw 'Start Menu registration failed. The installer did not complete.'}
}
if (-not $NoRegistration) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkplaceOrchestrator'
    New-Item -Path $key -Force | Out-Null
    $powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $properties = @{ DisplayName='Workplace Orchestrator'; DisplayVersion='1.1.0'; Publisher='Workplace Orchestrator'; InstallLocation=$destination; DisplayIcon=($exe+',0'); UninstallString=('"'+$powershell+'" -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $destination 'uninstall.ps1')+'"') }
    foreach ($name in $properties.Keys) { New-ItemProperty -Path $key -Name $name -Value $properties[$name] -PropertyType String -Force | Out-Null }
    foreach($name in @('NoModify','NoRepair')){New-ItemProperty -Path $key -Name $name -Value 1 -PropertyType DWord -Force | Out-Null}
    $appPath='HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\WorkplaceOrchestrator.exe'
    New-Item -Path $appPath -Force | Out-Null
    Set-Item -LiteralPath $appPath -Value $exe
    New-ItemProperty -Path $appPath -Name Path -Value $destination -Force | Out-Null
}
Write-Output ('Installed Workplace Orchestrator to ' + $destination)
Write-Output 'Workspace configuration is preserved during installation and uninstallation by default.'
if($Launch){Start-Process -FilePath $exe}
