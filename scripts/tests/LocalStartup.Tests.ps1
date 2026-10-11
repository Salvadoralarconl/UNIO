$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Clary-Startup.ps1')
$keys=@('Identity__Mode','Identity__Development__Enabled','Identity__Oidc__ClientId','Identity__Oidc__ClientSecret','Identity__Oidc__CallbackOrigin','Identity__FrontendOrigin')
$old=@{};foreach($key in $keys){$old[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$null,'Process')}
try {
 $env:Identity__Mode='Local';$env:Identity__FrontendOrigin='http://localhost:5174'
 Assert-ClaryLocalIdentity
 if($env:Identity__Oidc__ClientId -or $env:Identity__Oidc__ClientSecret){throw 'Local required Google credentials'}
 $env:Identity__FrontendOrigin='https://foreign.example'
 try{Assert-ClaryLocalIdentity;throw 'Foreign origin accepted'}catch{if($_.Exception.Message -eq 'Foreign origin accepted'){throw}}
 $env:Identity__FrontendOrigin='http://localhost:5174';$env:Identity__Mode='Unknown'
 try{Assert-ClaryLocalIdentity;throw 'Unknown mode accepted'}catch{if($_.Exception.Message -eq 'Unknown mode accepted'){throw}}
 Write-Output 'PASS Local startup without Google, exact frontend and unknown mode rejection'
}finally{foreach($key in $keys){[Environment]::SetEnvironmentVariable($key,$old[$key],'Process')}}
