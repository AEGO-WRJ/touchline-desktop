$ErrorActionPreference = 'Stop'
$compiler = (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe')
& $compiler /nologo /codepage:65001 /target:exe /out:"$PSScriptRoot\Tests.exe" /r:System.Web.Extensions.dll /r:System.Net.Http.dll "/resource:$PSScriptRoot\teams.json,Touchline.Teams" "$PSScriptRoot\Core.cs" "$PSScriptRoot\Names.cs" "$PSScriptRoot\Feed.cs" "$PSScriptRoot\TeamDirectory.cs" "$PSScriptRoot\FavoriteStore.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$PSScriptRoot\Tests.exe" "$PSScriptRoot\fixture-scoreboard.json"
exit $LASTEXITCODE
