param(
 [Parameter(Mandatory=$true)][string]$Issuer,
 [Parameter(Mandatory=$true)][string]$Subject,
 [string]$Dotnet="C:/Users/sapro/.dotnet/dotnet.exe"
)
$ErrorActionPreference='Stop'
# Use a provider-verified issuer and subject. No password or email is accepted.
# ConnectionStrings__Knowledge must be supplied through the local process environment.
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Knowledge)) { throw 'ConnectionStrings__Knowledge is required.' }
$taskOldIssuer=$env:Operator__Issuer; $taskOldSubject=$env:Operator__Subject
try {
 $env:Operator__Issuer=$Issuer; $env:Operator__Subject=$Subject
 & $Dotnet run --project (Join-Path $PSScriptRoot '../backend/Clary.Api') --no-launch-profile -- --initialize-operator
 if ($LASTEXITCODE -ne 0) { throw 'Operator initialization failed.' }
} finally { $env:Operator__Issuer=$taskOldIssuer; $env:Operator__Subject=$taskOldSubject }
