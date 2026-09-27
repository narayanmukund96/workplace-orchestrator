$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$release=Join-Path $root 'release'
$stage=Join-Path $release ('staging-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $stage,(Join-Path $stage 'dist'),(Join-Path $stage 'tests') -Force | Out-Null
foreach($name in @('README.md','build.ps1','install.ps1','uninstall.ps1','package.ps1')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $root 'src'),(Join-Path $root 'docs') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $root 'dist\WorkplaceOrchestrator.exe'),(Join-Path $root 'dist\WorkplaceOrchestrator.exe.config') -Destination (Join-Path $stage 'dist')
Get-ChildItem -LiteralPath (Join-Path $root 'tests') -File | Where-Object { $_.Extension -in @('.cs','.ps1') } | Copy-Item -Destination (Join-Path $stage 'tests')
$archive=Join-Path $release 'WorkplaceOrchestrator-1.0.0.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
Write-Output ('Package: '+$archive)
Get-FileHash -LiteralPath $archive -Algorithm SHA256
