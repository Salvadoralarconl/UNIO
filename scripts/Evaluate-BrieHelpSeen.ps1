param([Parameter(Mandatory)][string]$CandidateDll,[Parameter(Mandatory)][string]$ExpectedHash,[int[]]$Cases=@(2,3,5,11,16),[string]$RunName='brie-help-seen',[string]$HarnessRuntime='C:/Users/sapro/Documents/Codex/2026-10-10/task/live-harness-build/bin/LiveAcceptance/debug')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$taskRoot='C:/Users/sapro/Documents/Codex/2026-10-10/task'
$outputRoot=Join-Path $projectRoot ('artifacts/'+$RunName)
if($RunName -notmatch '^brie-help-[a-z0-9-]+$'){throw 'Invalid diagnostic run name'}
if(Test-Path -LiteralPath $outputRoot){throw 'Refuse to overwrite a diagnostic run'}
if((Get-FileHash -LiteralPath $CandidateDll).Hash -ne $ExpectedHash){throw 'Candidate hash mismatch'}
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$runtime=Join-Path $outputRoot 'runtime'
Copy-Item -LiteralPath $HarnessRuntime -Destination $runtime -Recurse
Copy-Item -LiteralPath $CandidateDll -Destination (Join-Path $runtime 'Clary.Api.dll')
if((Get-FileHash -LiteralPath (Join-Path $runtime 'Clary.Api.dll')).Hash -ne $ExpectedHash){throw 'Runtime hash mismatch'}
. (Join-Path $PSScriptRoot 'Clary-Startup.ps1')
Import-ClaryEnvironment (Join-Path $projectRoot '.env')
$env:CLARY_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=clary;Username=clary;Password='+$env:POSTGRES_PASSWORD
Set-Location -LiteralPath $taskRoot
foreach($number in $Cases){
 $id='H{0:d2}' -f $number
 & C:/Users/sapro/.dotnet/dotnet.exe (Join-Path $runtime 'LiveAcceptance.dll') --sealed-case $number *> (Join-Path $outputRoot ($id+'.console.txt'))
 $exitCode=$LASTEXITCODE
 $result=Get-Content -LiteralPath 'live-acceptance-results.json' -Raw | ConvertFrom-Json
 if(-not @($result.observations|Where-Object {$_.scenarioBegin -eq $id}).Count){throw 'Diagnostic result identity mismatch'}
 $resultPath=Join-Path $outputRoot ($id+'.json')
 Copy-Item -LiteralPath 'live-acceptance-results.json' -Destination $resultPath
 [ordered]@{scenario=$id;candidateSha256=$ExpectedHash;harnessSha256=(Get-FileHash -LiteralPath (Join-Path $runtime 'LiveAcceptance.dll')).Hash;exitCode=$exitCode;artifactSha256=(Get-FileHash -LiteralPath $resultPath).Hash;capturedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');scope='seen regression, disposable synthetic data; not an independent baseline or release qualification'}|ConvertTo-Json|Set-Content -LiteralPath ($resultPath+'.metadata.json')
 [ordered]@{scenario=$id;terminal=@($result.observations|Where-Object scenarioTerminal).Count -eq 1;automatedFailures=@($result.failures).Count;manualReviewPending=$true}|ConvertTo-Json -Compress|Write-Output
}
