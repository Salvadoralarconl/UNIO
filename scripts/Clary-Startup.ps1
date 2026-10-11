function Import-ClaryEnvironment([string]$Path) {
 $allowed=@('Identity__Mode','POSTGRES_PASSWORD','OLLAMA_BASE_URL','OLLAMA_GENERATION_MODEL','OLLAMA_EMBEDDING_MODEL','OLLAMA_EMBEDDING_DIMENSIONS','Identity__Development__Enabled','Identity__Oidc__Authority','Identity__Oidc__ClientId','Identity__Oidc__ClientSecret','Identity__Oidc__CallbackOrigin','Identity__FrontendOrigin')
 if(-not (Test-Path -LiteralPath $Path)){return}
 foreach($line in [IO.File]::ReadAllLines($Path)) {
  if($line -notmatch '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$'){continue}
  $key=$Matches[1];$value=$Matches[2].Trim()
  if($key -cnotin $allowed){continue}
  if($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))){$value=$value.Substring(1,$value.Length-2)}
  [Environment]::SetEnvironmentVariable($key,$value,'Process')
 }
}
function Assert-ClaryLocalOidc {
 if($env:Identity__Development__Enabled -ne 'false'){throw 'Local Google startup requires Identity__Development__Enabled=false in private configuration.'}
 foreach($key in @('Identity__Oidc__Authority','Identity__Oidc__ClientId','Identity__Oidc__ClientSecret')) {
  if([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key,'Process'))){throw "Missing private OIDC configuration: $key."}
 }
 if($env:Identity__Oidc__Authority -cne 'https://accounts.google.com'){throw 'Configure the trusted Google authority https://accounts.google.com.'}
 if($env:Identity__Oidc__CallbackOrigin -cne 'https://localhost:5081' -or $env:Identity__FrontendOrigin -cne 'http://localhost:5174'){throw 'Configure exact trusted origins https://localhost:5081 and http://localhost:5174.'}
}

function Assert-ClaryLocalIdentity {
 if([string]::IsNullOrWhiteSpace($env:Identity__Mode)){$env:Identity__Mode='Local'}
 if($env:Identity__Mode -ceq 'Oidc'){Assert-ClaryLocalOidc;return}
 if($env:Identity__Mode -cne 'Local'){throw 'Identity__Mode must be Local or Oidc.'}
 if([string]::IsNullOrWhiteSpace($env:Identity__FrontendOrigin)){$env:Identity__FrontendOrigin='http://localhost:5174'}
 if($env:Identity__FrontendOrigin -cne 'http://localhost:5174'){throw 'Configure exact trusted frontend origin http://localhost:5174.'}
 $env:Identity__Development__Enabled='false'
}
