param(
 [ValidateSet('Normal','Migrate','FreshBootstrap')][string]$Mode='Normal',
 [switch]$Compose,
 [ValidateSet('OpenAI','Ollama','Demo')][string]$Provider='Ollama'
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
. (Join-Path $PSScriptRoot 'Clary-Startup.ps1')
Import-ClaryEnvironment (Join-Path $projectRoot '.env')
$env:AI_PROVIDER=$Provider
if([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Knowledge)) {
 if([string]::IsNullOrWhiteSpace($env:POSTGRES_PASSWORD)){throw 'Private database configuration is required.'}
 $env:ConnectionStrings__Knowledge='Host=127.0.0.1;Port=5433;Database=clary;Username=clary;Password='+$env:POSTGRES_PASSWORD
}
if($Compose){throw 'Local identity HTTPS uses the existing Windows development certificate. Use local startup; Compose API startup is not a supported HTTPS rollout path.'}
$localSdk=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
$dotnetPath=if(Test-Path -LiteralPath $localSdk){$localSdk}else{'dotnet'}
$taskArguments=@('run','--project','backend/Clary.Api','--no-launch-profile','--')
switch($Mode) {
 'FreshBootstrap' {$taskArguments+='--fresh-bootstrap'}
 'Migrate' {$taskArguments+='--migrate'}
 'Normal' {
  Assert-ClaryLocalIdentity
  $env:ASPNETCORE_ENVIRONMENT='Development'
  & $dotnetPath dev-certs https --check
  if($LASTEXITCODE -ne 0){throw 'An existing valid localhost development certificate is required. No certificate or trust changes were made.'}
  if($Provider -eq 'OpenAI' -and [string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)){$env:OPENAI_API_KEY=[Environment]::GetEnvironmentVariable('OPENAI_API_KEY','User')}
  if($Provider -eq 'OpenAI' -and [string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)){throw 'OPENAI_API_KEY is required for the selected AI provider.'}
  $taskArguments+=@('--urls','https://localhost:5081')
 }
}
& $dotnetPath @taskArguments
if($LASTEXITCODE -ne 0){throw 'Clary operation failed; inspect local diagnostics privately.'}
