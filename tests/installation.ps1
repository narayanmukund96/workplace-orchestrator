param([switch]$LeaveInstalled)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$destination=Join-Path $env:LOCALAPPDATA 'Programs\WorkplaceOrchestrator'
$log=Join-Path $PSScriptRoot ('output\installation-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'.txt')
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw $Name};('PASS '+$Name) | Tee-Object -FilePath $log -Append}
$data=Join-Path $env:LOCALAPPDATA 'WorkplaceOrchestrator'
$before=@{};if(Test-Path -LiteralPath $data){Get-ChildItem -LiteralPath $data -Filter 'workspaces.db*' | ForEach-Object {$before[$_.Name]=(Get-FileHash -LiteralPath $_.FullName).Hash}}
& (Join-Path $root 'install.ps1')
$exe=Join-Path $destination 'WorkplaceOrchestrator.exe'
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
Check ($version.ProductName -eq 'Workplace Orchestrator' -and $version.FileVersion -eq '1.1.0.0') 'Production product name and version metadata'
$shell=New-Object -ComObject WScript.Shell
$link=Join-Path ([Environment]::GetFolderPath('Programs')) 'Workplace Orchestrator.lnk'
try{$shortcut=$shell.CreateShortcut($link);Check ($shortcut.TargetPath -eq $exe) 'Start Menu shortcut targets installed production executable';Check ($shortcut.IconLocation -eq ($exe+',0')) 'Start Menu shortcut uses production icon';[Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)|Out-Null}finally{[Runtime.InteropServices.Marshal]::ReleaseComObject($shell)|Out-Null}
$explorer=New-Object -ComObject Shell.Application
try{$folder=$explorer.NameSpace([IO.Path]::GetDirectoryName($link));$item=$folder.ParseName([IO.Path]::GetFileName($link));Check ($item.ExtendedProperty('System.AppUserModel.ID') -eq 'Workplace.Orchestrator') 'Stable Start Menu AppUserModelID'}finally{[Runtime.InteropServices.Marshal]::ReleaseComObject($explorer)|Out-Null}
$powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$apps=''
for($attempt=0;$attempt -lt 6;$attempt++){$apps=& $powershell -NoProfile -NonInteractive -Command "Get-StartApps -Name 'Workplace Orchestrator' | ConvertTo-Json -Compress";if($apps -match 'Workplace.Orchestrator'){break};Start-Sleep -Seconds 2}
Check ([bool]($apps -match 'Workplace.Orchestrator')) 'Windows Start/Search application catalogue contains Workplace Orchestrator'
$registration=Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkplaceOrchestrator'
Check ($registration.DisplayName -eq 'Workplace Orchestrator' -and $registration.DisplayVersion -eq '1.1.0') 'Windows Installed Apps uninstall registration'
Check (@(Get-ChildItem -LiteralPath $destination -Filter '*.exe').Count -eq 1) 'Installed folder contains only the production executable'
if(-not $LeaveInstalled){& (Join-Path $destination 'uninstall.ps1');Check (-not(Test-Path -LiteralPath $link) -and -not(Test-Path -LiteralPath $exe)) 'Uninstall removes product executable and Start Menu shortcut';Check (-not(Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WorkplaceOrchestrator')) 'Uninstall removes Installed Apps registration'}
foreach($name in $before.Keys){Check ((Get-FileHash -LiteralPath (Join-Path $data $name)).Hash -eq $before[$name]) ('User configuration preserved: '+$name)}
'RESULT installation checks passed' | Tee-Object -FilePath $log -Append
