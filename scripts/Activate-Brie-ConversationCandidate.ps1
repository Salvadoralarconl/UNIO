param(
 [Parameter(Mandatory)][int]$ExpectedCurrentPid,
 [Parameter(Mandatory)][string]$ExpectedCurrentDll,
 [Parameter(Mandatory)][string]$VerifiedCandidateHash,
 [string]$CandidateDll,
 [switch]$PrepareOnly
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$candidate=if($CandidateDll){[IO.Path]::GetFullPath($CandidateDll)}else{Join-Path $projectRoot 'artifacts/brie-current-candidate/publish/Clary.Api/release/Clary.Api.dll'}
$sdk=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
$backendRoot=Join-Path $projectRoot 'backend/Clary.Api'
function Normalize-ApiPath([string]$path){[IO.Path]::GetFullPath($path.Replace('/','\')).Replace('\\','\')}
if((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -cne $VerifiedCandidateHash){throw 'Candidate differs from the verified snapshot.'}
$running=Get-CimInstance Win32_Process -Filter "ProcessId=$ExpectedCurrentPid"
if($null -eq $running -or (Normalize-ApiPath $running.ExecutablePath) -ine (Normalize-ApiPath $sdk) -or -not $running.CommandLine.Replace('/','\').Replace('\\','\').Contains((Normalize-ApiPath $ExpectedCurrentDll))){throw 'Running API changed; revalidate before activation.'}
$expectedRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if(-not $candidate.StartsWith($expectedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Candidate API is outside this workspace artifacts directory.'}
$currentDll=[IO.Path]::GetFullPath($ExpectedCurrentDll)
if(-not $currentDll.StartsWith($expectedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Expected API is outside this workspace artifacts directory.'}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot ('artifacts/brie-conversation-rollback-'+$stamp)))
if(-not $backupRoot.StartsWith($expectedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid backup target.'}
$backupApi=Join-Path $backupRoot 'api'
New-Item -ItemType Directory -Path $backupApi -ErrorAction Stop | Out-Null
Get-ChildItem -LiteralPath (Split-Path $currentDll -Parent) -File | Where-Object {$_.Extension -ne '.log'} | Copy-Item -Destination $backupApi
# Startup reads SQL and optional sample files relative to the binary directory.
# Preserve those existing files so rollback can start with the same dependencies.
foreach($folderName in @('database','samples')){
 $sourceFolder=Join-Path (Split-Path $currentDll -Parent) $folderName
 if(Test-Path -LiteralPath $sourceFolder -PathType Container){
  Copy-Item -LiteralPath $sourceFolder -Destination $backupApi -Recurse
  foreach($sourceFile in (Get-ChildItem -LiteralPath $sourceFolder -Recurse -File)){
   $relative=$sourceFile.FullName.Substring($sourceFolder.Length+1)
   $backupFile=Join-Path (Join-Path $backupApi $folderName) $relative
   if((Get-FileHash -LiteralPath $sourceFile).Hash -ne (Get-FileHash -LiteralPath $backupFile).Hash){throw 'Rollback dependency copy did not verify.'}
  }
 }
}
$previous=Join-Path $backupApi 'Clary.Api.dll'
if((Get-FileHash -LiteralPath $previous).Hash -ne (Get-FileHash -LiteralPath $currentDll).Hash){throw 'Rollback copy did not verify.'}
if($PrepareOnly){Write-Output ('Prepared verified binary rollback: '+$backupRoot);exit 0}
. (Join-Path $PSScriptRoot 'Clary-Startup.ps1')
Import-ClaryEnvironment (Join-Path $projectRoot '.env')
Assert-ClaryLocalIdentity
$env:AI_PROVIDER='Ollama'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ConnectionStrings__Knowledge='Host=127.0.0.1;Port=5433;Database=clary;Username=clary;Password='+$env:POSTGRES_PASSWORD
function Start-Api([string]$dll,[string]$prefix){
 Start-Process -FilePath $sdk -ArgumentList @($dll,'--urls','https://localhost:5081','--contentRoot',$backendRoot) -WorkingDirectory $backendRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $backupRoot ($prefix+'.log')) -RedirectStandardError (Join-Path $backupRoot ($prefix+'-errors.log'))
}
function Wait-Api {
 for($attempt=0;$attempt -lt 40;$attempt++){
  try {$health=Invoke-RestMethod 'https://localhost:5081/api/health' -SkipCertificateCheck -TimeoutSec 2;if($health.status -eq 'ok' -and $health.liveReady){return $health}}catch{}
  Start-Sleep -Milliseconds 500
 }
 return $null
}
# Binary-only update: no migrations, seeds, account changes, or record mutations.
Stop-Process -Id $ExpectedCurrentPid
$started=$null
try {$started=Start-Api $candidate 'candidate';$health=Wait-Api}catch{$health=$null}
if($null -eq $health){
 if($null -ne $started -and (Get-Process -Id $started.Id -ErrorAction SilentlyContinue)){Stop-Process -Id $started.Id}
 $restored=Start-Api $previous 'rollback'
 if($null -eq (Wait-Api)){throw 'Candidate and rollback startup failed; inspect private logs.'}
 throw ('Candidate startup failed; verified prior binary restored as PID '+$restored.Id)
}
Write-Output ('Activated verified candidate PID '+$started.Id+'; rollback '+$backupRoot)
$health | Select-Object status,origin,liveReady | ConvertTo-Json
