param(
 [Parameter(Mandatory=$true)][string]$OutputFile,
 [string]$Dotnet="C:/Users/sapro/.dotnet/dotnet.exe"
)
$ErrorActionPreference='Stop'
# The offline branch accepts no password, username, role or provider binding.
# Use a new file beneath $env:LOCALAPPDATA/Brie. The API creates a user-only ACL.
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Knowledge)) { throw 'ConnectionStrings__Knowledge is required.' }
$taskOldOutput=$env:BRIE_OPERATOR_OUTPUT_FILE
try {
 $env:BRIE_OPERATOR_OUTPUT_FILE=$OutputFile
 & $Dotnet run --project (Join-Path $PSScriptRoot '../backend/Clary.Api') --no-launch-profile -- --initialize-local-operator
 if ($LASTEXITCODE -ne 0) { throw 'Local Operator initialization failed.' }
} finally { $env:BRIE_OPERATOR_OUTPUT_FILE=$taskOldOutput }
