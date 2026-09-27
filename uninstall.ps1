param([switch]$RemoveData)
$ErrorActionPreference = 'Stop'
$installation = [IO.Path]::GetFullPath($PSScriptRoot)
$marker = Join-Path $installation '.workplace-orchestrator-install'
if (-not (Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw).Trim() -notin @('WorkplaceOrchestrator 1.0.0','WorkplaceOrchestrator 1.1.0')) { throw 'This script must run from a marked Workplace Orchestrator installation.' }
$exe = Join-Path $installation 'WorkplaceOrchestrator.exe'
$running = Get-Process -Name 'WorkplaceOrchestrator' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) { throw 'Close Workplace Orchestrator before uninstalling. Your applications can remain open.' }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Workplace Orchestrator.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -eq $exe) { Remove-Item -LiteralPath $shortcutPath -Force }
        [Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
    } finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null }
}
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkplaceOrchestrator'
if ((Test-Path -LiteralPath $key) -and (Get-ItemProperty -LiteralPath $key).InstallLocation -eq $installation) { Remove-Item -LiteralPath $key }
$appPath='HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\WorkplaceOrchestrator.exe'
if((Test-Path -LiteralPath $appPath) -and (Get-Item -LiteralPath $appPath).GetValue('') -eq $exe){Remove-Item -LiteralPath $appPath}
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if(Test-Path -LiteralPath $runKey){$command=(Get-ItemProperty -LiteralPath $runKey -Name WorkplaceOrchestrator -ErrorAction SilentlyContinue).WorkplaceOrchestrator;if($command -eq ('"'+$exe+'" --background')){Remove-ItemProperty -LiteralPath $runKey -Name WorkplaceOrchestrator}}
if ($RemoveData) {
    $localRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')
    $dataTarget = [IO.Path]::GetFullPath((Join-Path $localRoot 'WorkplaceOrchestrator'))
    if ($dataTarget -ne ($localRoot+'\WorkplaceOrchestrator') -or [IO.Path]::GetDirectoryName($dataTarget) -ne $localRoot) { throw 'Refusing an unexpected configuration path.' }
    if (Test-Path -LiteralPath $dataTarget) {
        $item = Get-Item -LiteralPath $dataTarget -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to recursively delete a redirected configuration folder.' }
        Remove-Item -LiteralPath $dataTarget -Recurse -Force
    }
}
# Delete only this product's known files; never recursively remove an installation folder.
foreach ($name in @('WorkplaceOrchestrator.exe','WorkplaceOrchestrator.exe.config','WorkplaceOrchestrator.ico','.workplace-orchestrator-install','uninstall.ps1')) {
    $target = [IO.Path]::GetFullPath((Join-Path $installation $name))
    if ([IO.Path]::GetDirectoryName($target) -ne $installation) { throw 'Refusing an unexpected uninstall path.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
}
Write-Output 'Workplace Orchestrator was removed.'
if (-not $RemoveData) { Write-Output 'Your workspace configuration was preserved.' }
