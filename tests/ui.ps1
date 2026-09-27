$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$output=Join-Path $PSScriptRoot ('output\ui-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runtime=Join-Path $env:TEMP ('WorkplaceOrchestrator.Tests\ui-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$exe=Join-Path $runtime 'UiSmoke.exe'
$references=@('System.dll','System.Core.dll','System.Xaml.dll',(Join-Path $framework 'WPF\WindowsBase.dll'),(Join-Path $framework 'WPF\PresentationCore.dll'),(Join-Path $framework 'WPF\PresentationFramework.dll'),(Join-Path $root 'candidate\WorkplaceOrchestrator.exe'))
$compile=@('/nologo','/target:winexe','/platform:x64',('/out:'+$exe))
$compile+=$references | ForEach-Object { '/reference:'+$_ }
$compile+=(Join-Path $PSScriptRoot 'UiSmoke.cs')
& (Join-Path $framework 'csc.exe') @compile
if($LASTEXITCODE -ne 0){throw 'UI smoke test compilation failed.'}
Copy-Item -LiteralPath (Join-Path $root 'candidate\WorkplaceOrchestrator.exe') -Destination $runtime
$process=Start-Process -FilePath $exe -ArgumentList ('"'+$output+'"') -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(45000)){ $process.Kill(); throw 'UI test exceeded 45 seconds.' }
Get-Content -LiteralPath (Join-Path $output 'ui-results.txt')
if($process.ExitCode -ne 0){throw 'UI smoke test failed.'}
