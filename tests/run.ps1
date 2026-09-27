param([switch]$Native,[string]$BinaryDirectory='candidate')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$stamp=[DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')
$out=Join-Path $env:TEMP ('WorkplaceOrchestrator.Tests\'+$stamp)
$run=Join-Path $PSScriptRoot ('output\'+$stamp)
New-Item -ItemType Directory -Force -Path $out,$run | Out-Null
$product=Join-Path $root ($BinaryDirectory+'\WorkplaceOrchestrator.exe')
Copy-Item -LiteralPath $product -Destination $out
$testExe=Join-Path $out 'Tests.exe'
& $compiler /nologo /target:exe /platform:x64 /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Management.dll /r:System.Web.Extensions.dll ('/r:'+$product) ('/r:'+(Join-Path $framework 'WPF\PresentationCore.dll')) ('/r:'+(Join-Path $framework 'WPF\WindowsBase.dll')) ('/out:'+$testExe) (Join-Path $PSScriptRoot 'Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Test compilation failed.'}
if($Native){
    $helper=Join-Path $out 'OrchestratorControlledTest.exe'
    & $compiler /nologo /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/out:'+$helper) (Join-Path $PSScriptRoot 'TestApp.cs')
    if($LASTEXITCODE -ne 0){throw 'Test app compilation failed.'}
    & $testExe $run $helper | Tee-Object -FilePath (Join-Path $run 'results.txt')
}else{
    & $testExe $run | Tee-Object -FilePath (Join-Path $run 'results.txt')
}
if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
