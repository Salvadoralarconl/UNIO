$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Clary-Startup.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('clary-env-'+[guid]::NewGuid()+'.env')
$keys=@('Identity__Mode','Identity__Development__Enabled','Identity__Oidc__Authority','Identity__Oidc__ClientId','Identity__Oidc__ClientSecret','Identity__Oidc__CallbackOrigin','Identity__FrontendOrigin','CLARY_UNSAFE_TEST')
$old=@{};foreach($key in $keys){$old[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$null,'Process')}
try {
 @'
Identity__Mode=Oidc
Identity__Development__Enabled=false
Identity__Oidc__Authority=https://accounts.google.com
Identity__Oidc__ClientId=fixture-client
Identity__Oidc__ClientSecret='fixture-$([IO.File]::WriteAllText("never","execute"))'
Identity__Oidc__CallbackOrigin=https://localhost:5081
Identity__FrontendOrigin=http://localhost:5174
CLARY_UNSAFE_TEST=must-not-import
'@ | Set-Content $fixture
 $output=Import-ClaryEnvironment $fixture
 if($output){throw 'Environment import exposed output'}
 if($env:Identity__Mode -cne 'Oidc'){throw 'Identity mode not imported'}
 if($env:CLARY_UNSAFE_TEST){throw 'Nonallowlisted key imported'}
 if($env:Identity__Oidc__ClientSecret -ne 'fixture-$([IO.File]::WriteAllText("never","execute"))'){throw 'Literal secret changed or evaluated'}
 Assert-ClaryLocalOidc
 $env:Identity__Oidc__ClientSecret=$null
 try{Assert-ClaryLocalOidc;throw 'Missing secret accepted'}catch{if($_.Exception.Message -eq 'Missing secret accepted'){throw}}
 $env:Identity__Oidc__ClientSecret='fixture'
 $env:Identity__Oidc__CallbackOrigin='https://evil.example'
 try{Assert-ClaryLocalOidc;throw 'Foreign callback accepted'}catch{if($_.Exception.Message -eq 'Foreign callback accepted'){throw}}
 Write-Output 'PASS private allowlist, literal handling, missing configuration and trusted origin rejection'
}finally{Remove-Item -LiteralPath $fixture;foreach($key in $keys){[Environment]::SetEnvironmentVariable($key,$old[$key],'Process')}}
