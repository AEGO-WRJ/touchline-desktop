param([string]$OutputDirectory = "$PSScriptRoot\dist", [string]$ExecutableName = 'Touchline.exe')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$assets = Join-Path $PSScriptRoot 'assets'
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('System.Web.Extensions.dll', 'System.Net.Http.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Xaml.dll')
$references += @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll') | ForEach-Object { Join-Path "$framework\WPF" $_ }
$arguments = @(
    '/nologo', '/codepage:65001', '/target:winexe', '/optimize+', '/platform:anycpu'
    "/out:$(Join-Path $OutputDirectory $ExecutableName)"
    "/win32icon:$assets\touchline.ico"
    "/resource:$assets\touchline.ico,Touchline.Icon"
    "/resource:$assets\teams.json,Touchline.Teams"
    "/resource:$assets\windows-update-public.xml,Touchline.UpdateKey"
)
$arguments += $references | ForEach-Object { "/r:$_" }
$arguments += $sources
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Built $(Join-Path $OutputDirectory $ExecutableName)"
