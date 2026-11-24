param([string]$Iscc = "ISCC.exe")
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $params=@('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001','/win32icon:src/sunflower.ico','/out:dist/LigPrint.exe','/resource:src/public.xml,LigPrint.PublicKey','/resource:src/sunflower.ico,LigPrint.Icon','/resource:src/toolbar.png,LigPrint.ToolbarIcon','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Data.dll')
    foreach($dll in Get-ChildItem vendor/*.dll){$params+="/r:$($dll.FullName)";$params+="/resource:$($dll.FullName),$($dll.Name)"}
    foreach($notice in Get-ChildItem vendor/*LICENSE.txt){$params+="/resource:$($notice.FullName),$($notice.Name)"}
    $params+=(Get-ChildItem src/*.cs).FullName
    & $csc @params
    if($LASTEXITCODE){throw "应用编译失败"}
    & $Iscc /Q installer.iss
    if($LASTEXITCODE){throw "安装包编译失败"}
} finally { Pop-Location }




