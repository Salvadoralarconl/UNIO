$ErrorActionPreference='Stop'
$folder=Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\epson\originals'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$manuals=@{
 'TM-T88IV-users-manual.pdf'='https://files.support.epson.com/pdf/pos/bulk/tm-t88iv_um_ww_02.pdf'
 'TM-T88V-users-manual.pdf'='https://files.support.epson.com/pdf/pos/bulk/tm-t88v_hwum_en_02.pdf'
 'TM-T20II-users-manual.pdf'='https://files.support.epson.com/pdf/pos/bulk/tm-t20ii_hwum_en_03.pdf'
 'TM-T20III-technical-reference-revF.pdf'='https://download4.epson.biz/sec_pubs/bs/pdf/TM-T20III_trg_en_revF.pdf'
 'TM-m30-technical-reference-revL.pdf'='https://download4.epson.biz/sec_pubs/bs/pdf/TM-m30_trg_en_revL.pdf'
 'TM-m30-cannot-print.html'='https://download4.epson.biz/sec_pubs/bs/html/m000943/en/chap09_1.html'
 'TM-m30-led-diagnostics.html'='https://download4.epson.biz/sec_pubs/bs/html/m000943/en/chap09_2.html'
 'TM-m30-paper-jams.html'='https://download4.epson.biz/sec_pubs/bs/html/m000943/en/chap09_3.html'
}
foreach($entry in $manuals.GetEnumerator()) {
 $path=Join-Path $folder $entry.Key
 try { Invoke-WebRequest -Uri $entry.Value -OutFile $path -TimeoutSec 120 } catch { Write-Warning ('Could not download '+$entry.Key+'. Open the official URL manually: '+$entry.Value); continue }
 if($entry.Key.EndsWith('.pdf')) {
  $stream=[IO.File]::OpenRead($path)
  try { $bytes=New-Object byte[] 5; $null=$stream.Read($bytes,0,5); if([Text.Encoding]::ASCII.GetString($bytes) -ne '%PDF-'){throw 'Download is not a PDF.'} } finally { $stream.Dispose() }
 }
 Write-Output ('Downloaded '+$entry.Key)
}
