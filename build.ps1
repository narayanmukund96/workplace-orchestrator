param([switch]$Test,[string]$OutputDirectory='candidate')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 is required on 64-bit Windows 10/11.' }
$out = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Path $out -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Management.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll',(Join-Path $framework 'WPF\WindowsBase.dll'), (Join-Path $framework 'WPF\PresentationCore.dll'), (Join-Path $framework 'WPF\PresentationFramework.dll'))
$argsList = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4',('/out:' + (Join-Path $out 'WorkplaceOrchestrator.exe')),('/win32manifest:' + (Join-Path $root 'src\app.manifest')),('/resource:' + (Join-Path $root 'src\MainWindow.xaml') + ',MainWindow.xaml'))
$argsList += $references | ForEach-Object { '/reference:' + $_ }
$argsList += '/win32icon:'+(Join-Path $root 'assets\WorkplaceOrchestrator.ico')
$argsList += '/resource:'+(Join-Path $root 'assets\WorkplaceOrchestrator.ico')+',WorkplaceOrchestrator.ico'
$argsList += Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $root 'src\WorkplaceOrchestrator.exe.config') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\WorkplaceOrchestrator.ico') -Destination $out -Force
Write-Output ('Built: ' + (Join-Path $out 'WorkplaceOrchestrator.exe'))
if ($Test) {
    & (Join-Path $root 'tests\run.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
