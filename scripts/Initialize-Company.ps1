param([string]$CompanyName,[string]$IdentitySubject,[string]$Issuer)
$ErrorActionPreference='Stop'
throw 'Company bootstrap is superseded by sole-Operator provisioning. Migrate first; verify the intended Google account; bind its exact trusted issuer/subject with Initialize-Operator.ps1. Create companies and assign their Administrator through the Operator dashboard. No company, account or membership was changed.'
