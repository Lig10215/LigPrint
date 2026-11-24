param([string]$Directory=(Join-Path $env:TEMP ("LigPrint-verify-"+[guid]::NewGuid())))
# Run in Windows PowerShell x64. Outputs stay in the selected local directory.
$ErrorActionPreference='Stop'
[void](New-Item -ItemType Directory -Path $Directory -Force)
$Directory=(Resolve-Path -LiteralPath $Directory).Path
$root=Split-Path $PSScriptRoot
$exe=Join-Path $root 'dist/LigPrint.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
[LigPrint.Startup]::ResolveLibraries()
$book=[LigPrint.ExcelBook]::Read((Join-Path $PSScriptRoot 'fixtures/labels.xlsx'))
if ($book.Count -ne 2 -or $book[0].Rows.Count -ne 2 -or $book[0].Rows[0][1] -ne '000012'){throw 'Excel import'}
$legacy=[LigPrint.ExcelBook]::Read((Join-Path $PSScriptRoot 'fixtures/legacy.xls'))
if($legacy[0].Rows[0][0] -ne '00042'){throw 'BIFF XLS format'}
$file=[LigPrint.LabelFile]::new()
$file.Data=$book[0]
foreach ($kind in @('文本','二维码','条形码')){
    $item=[LigPrint.Element]::new()
    $item.Type=$kind
    $item.Field='编码'
    $item.X=2
    $item.Y=2
    if($kind -eq '二维码'){$item.X=32;$item.W=24;$item.H=24}
    if($kind -eq '条形码'){$item.Y=26;$item.W=54;$item.H=10}
    $file.Items.Add($item)
}
$template=Join-Path $Directory 'roundtrip.lig'
$file.Save($template)
$file.Save($template)
$loaded=[LigPrint.LabelFile]::Load($template)
if ($loaded.Items[0].Value($loaded.Data,1) -ne '000013'){throw 'template round trip'}
foreach ($bad in @('https://invalid','\\invalid\example\labels.xlsx')){
    $rejected=$false
    try{[LigPrint.Disk]::Local($bad)}catch{$rejected=$true}
    if(!$rejected){throw 'remote path accepted'}
}
[LigPrint.Printing]::Run($loaded,[LigPrint.LabelPdf]::QueueName,0,1,2,(Join-Path $Directory 'labels.pdf'))
$queues=[LigPrint.Printing]::Queues()
$system=@(Get-CimInstance Win32_Printer)
$difference=Compare-Object -ReferenceObject @($system | ForEach-Object Name | Sort-Object) -DifferenceObject @($queues | Sort-Object)
if($difference){throw "Installed queue mismatch: $difference"}
$preferred=[LigPrint.PrinterQueues]::PreferredIndex($queues,$null)
$default=$system | Where-Object Default
if($default -and $queues[$preferred] -ne $default.Name){throw 'System default was not selected'}
"Installed queues: $($queues -join ', ')"
if($queues.Contains('Microsoft Print to PDF')){
    $driverPdf=Join-Path $Directory 'windows-driver.pdf'
    [LigPrint.Printing]::Run($loaded,'Microsoft Print to PDF',0,1,1,$driverPdf)
    if(!(Test-Path $driverPdf) -or (Get-Item $driverPdf).Length -lt 100){throw 'Windows driver job'}
    'PASS  Windows spooler -> Microsoft Print to PDF.'
}
$png=Join-Path $Directory 'layout.png'
$bitmap=[Drawing.Bitmap]::new(600,400)
try{
    $g=[Drawing.Graphics]::FromImage($bitmap)
    try{$g.Clear([Drawing.Color]::White);$g.DrawRectangle([Drawing.Pens]::Black,12,12,575,375)}
    finally{$g.Dispose()}
    $bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
}finally{$bitmap.Dispose()}
$converted=[LigPrint.DLabelLayout]::ConvertPng($png,60,40)
$converted.Data=$book[0]
$converted.Items.Add($loaded.Items[0])
$layoutPath=Join-Path $Directory 'converted.lig'
$converted.Save($layoutPath)
$restored=[LigPrint.LabelFile]::Load($layoutPath)
if($restored.Width -ne 60 -or $restored.Height -ne 40 -or !$restored.Items[0].Locked -or
    [Convert]::ToBase64String($restored.Items[0].ImageBytes) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($png)) -or
    $restored.Items[1].Value($restored.Data,1) -ne '000013'){throw 'Converted layout'}
[LigPrint.Printing]::Run($restored,[LigPrint.LabelPdf]::QueueName,0,1,1,(Join-Path $Directory 'converted.pdf'))
'PASS  PNG fixed artwork -> editable LIG with Excel overlay -> print.'
[LigPrint.Audit]::Write('verify',4)
$log=Get-ChildItem "$env:LOCALAPPDATA/LigPrint/Logs/*-$PID.lplog" | Select-Object -Last 1
$decoded=& "$PSScriptRoot/Decode-Log.ps1" $log.FullName
if (($decoded -join ' ') -notmatch 'verify\s+4'){throw 'log verification'}
Add-Type -AssemblyName System.Windows.Forms
[Windows.Forms.Application]::EnableVisualStyles()
$form=[LigPrint.Studio]::new()
try {
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $img=New-Object Drawing.Bitmap($form.Width,$form.Height)
    try {$form.DrawToBitmap($img,$form.ClientRectangle);$img.Save((Join-Path $Directory 'editor.png'))} finally {$img.Dispose()}
} finally {$form.Dispose()}
'PASS  Excel sheets, formatted identifiers, template overwrite and reload, local path checks, four-page print job, log decoder, editor startup.'





# Compile the GUI regression harness outside the release directory.
$harness=Join-Path $env:TEMP ("LigPrint-ui-test-"+[guid]::NewGuid())
[void](New-Item -ItemType Directory -Path $harness)
Push-Location $harness
try {
    Copy-Item -LiteralPath $exe -Destination 'LigPrint.exe'
    $source=Join-Path $PSScriptRoot 'VerifyEditor.cs'
    & "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /platform:x64 /r:LigPrint.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:VerifyEditor.exe $source
    if($LASTEXITCODE){throw 'UI test build'}
    & .\VerifyEditor.exe $Directory
    if($LASTEXITCODE){throw 'UI regression'}
} finally {
    Pop-Location
    foreach ($name in 'VerifyEditor.exe','LigPrint.exe'){
        $scratch=Join-Path $harness $name
        if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch}
    }
    if(!(Get-ChildItem -LiteralPath $harness -Force)){Remove-Item -LiteralPath $harness}
}
