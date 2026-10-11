$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
. (Join-Path $PSScriptRoot 'Clary-Startup.ps1')
Import-ClaryEnvironment (Join-Path $projectRoot '.env')
Assert-ClaryLocalIdentity
$env:AI_PROVIDER='Ollama'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ConnectionStrings__Knowledge='Host=127.0.0.1;Port=5433;Database=clary;Username=clary;Password='+$env:POSTGRES_PASSWORD
$candidate=Join-Path $projectRoot 'artifacts/brie-activation-final/Clary.Api.dll'
$backendRoot=Join-Path $projectRoot 'backend/Clary.Api'
$sdk=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
$expected=Join-Path $backendRoot 'bin/Debug/net10.0/Clary.Api.exe'
$running=Get-Process -Id 25860 -ErrorAction Stop
if($running.Path -cne $expected){throw 'API process changed; revalidate before activation.'}
$runner=Get-CimInstance Win32_Process -Filter 'ProcessId=47636'
if($runner.ExecutablePath -cne $sdk -or $runner.CommandLine -notmatch 'run.*Clary.Api'){throw 'API launcher changed; revalidate before activation.'}
if(-not(Test-Path -LiteralPath $candidate)){throw 'Verified candidate is missing.'}
# Explicit additive migration has already been rehearsed on a restored backup.
& $sdk $candidate --migrate
if($LASTEXITCODE -ne 0){throw 'Migration failed; the running API was left in place.'}
Stop-Process -Id 47636
if(Get-Process -Id 25860 -ErrorAction SilentlyContinue){Stop-Process -Id 25860}
$logRoot=Join-Path $projectRoot 'artifacts/brie-activation-final'
$started=Start-Process -FilePath $sdk -ArgumentList @($candidate,'--urls','https://localhost:5081','--contentRoot',$backendRoot) -WorkingDirectory $backendRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logRoot 'runtime.log') -RedirectStandardError (Join-Path $logRoot 'runtime-errors.log')
function Wait-Api {
 for($attempt=0;$attempt -lt 40;$attempt++){
  try {return Invoke-RestMethod 'https://localhost:5081/api/health' -SkipCertificateCheck -TimeoutSec 2} catch {Start-Sleep -Milliseconds 500}
 }
 return $null
}
$health=Wait-Api
if($null -eq $health){
 if(Get-Process -Id $started.Id -ErrorAction SilentlyContinue){Stop-Process -Id $started.Id}
 $previous=Join-Path $projectRoot 'artifacts/brie-rollback-20261010/api/Clary.Api.exe'
 Start-Process -FilePath $previous -ArgumentList @('--urls','https://localhost:5081','--contentRoot',$backendRoot) -WorkingDirectory $backendRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot 'rollback.log') -RedirectStandardError (Join-Path $logRoot 'rollback-errors.log') | Out-Null
 if($null -eq (Wait-Api)){throw 'Candidate and rollback startup failed; inspect private runtime logs.'}
 throw 'Candidate startup failed; previous API restored. Additive task tables preserved.'
}
Write-Output ('Activated API process: '+$started.Id)
$health | Select-Object status,origin,liveReady | ConvertTo-Json
