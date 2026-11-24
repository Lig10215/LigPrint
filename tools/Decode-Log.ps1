param(
    [Parameter(Mandatory=$true)][string]$Log,
    [string]$PrivateKey = (Join-Path $PSScriptRoot 'maintenance-private.xml')
)
$ErrorActionPreference='Stop'
$key = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$key.PersistKeyInCsp=$false
try {
    $key.FromXmlString((Get-Content -LiteralPath $PrivateKey -Raw))
    foreach($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $Log).Path)) {
        if($line.Trim()){
            $cipher=[Convert]::FromBase64String($line)
            [Text.Encoding]::UTF8.GetString($key.Decrypt($cipher,$true))
        }
    }
} finally { $key.Dispose() }


