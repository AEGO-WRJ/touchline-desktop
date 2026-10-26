param([string]$OutputDirectory = "$PSScriptRoot\dist", [string]$ExecutableName = 'Touchline.exe')
$ErrorActionPreference = 'Stop'
$compiler = (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe')
$wpf = (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF')
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu /out:"$OutputDirectory\$ExecutableName" "/win32icon:$PSScriptRoot\assets\touchline.ico" "/resource:$PSScriptRoot\assets\touchline.ico,Touchline.Icon" "/resource:$PSScriptRoot\teams.json,Touchline.Teams" /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" /r:System.Xaml.dll "$PSScriptRoot\Core.cs" "$PSScriptRoot\Names.cs" "$PSScriptRoot\Feed.cs" "$PSScriptRoot\TeamDirectory.cs" "$PSScriptRoot\FavoriteStore.cs" "$PSScriptRoot\Preferences.cs" "$PSScriptRoot\BadgeStore.cs" "$PSScriptRoot\App.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Built $OutputDirectory\$ExecutableName"
