param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Tag,
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$KeyPath
)
$ErrorActionPreference = 'Stop'
$file = Get-Item -LiteralPath $Executable
if ([Reflection.AssemblyName]::GetAssemblyName($file.FullName).Version.ToString(3) -ne $Version) { throw 'Executable version does not match.' }
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
try {
    $rsa.FromXmlString([IO.File]::ReadAllText((Resolve-Path -LiteralPath $KeyPath)))
    $public = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'assets\windows-update-public.xml'))
    if ($rsa.ToXmlString($false) -ne $public) { throw 'Signing key does not match the app public key.' }
    $payload = [ordered]@{
        Version = $Version; Platform = 'windows'
        Url = "https://github.com/AEGO-WRJ/touchline-desktop/releases/download/$Tag/Touchline.exe"
        Size = $file.Length; Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    } | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $envelope = [ordered]@{ Payload = [Convert]::ToBase64String($bytes); Signature = [Convert]::ToBase64String($rsa.SignData($bytes, 'SHA256')) } | ConvertTo-Json
    $destination = Join-Path $PSScriptRoot 'windows'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $destination 'update.json'), $envelope, [Text.UTF8Encoding]::new($false))
    Write-Output 'Signed windows/update.json. Publish the exact executable before pushing this manifest.'
} finally { $rsa.Dispose() }
