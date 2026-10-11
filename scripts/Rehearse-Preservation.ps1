param([string]$BackupRoot=(Join-Path $env:USERPROFILE 'Clary-Lab-Backups'),[switch]$RehearseDanglingReplacementRepair)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Clary-Startup.ps1')
Import-ClaryEnvironment (Join-Path $projectRoot '.env')
if([string]::IsNullOrWhiteSpace($env:POSTGRES_PASSWORD)){throw 'Private database configuration required.'}
$env:PGPASSWORD=$env:POSTGRES_PASSWORD
$docker='C:/Program Files/Docker/Docker/resources/bin/docker.exe'
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$copy='clary_rehearsal_'+$stamp.Replace('-','_')
$backup=Join-Path ([IO.Path]::GetFullPath($BackupRoot)) $stamp
if($backup.StartsWith($projectRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Backups must be outside the project.'}
$null=New-Item -ItemType Directory -Path $backup
function Invoke-LabSql([string]$Database,[string]$Sql) {
 $result=& $docker exec -e PGPASSWORD clary-lab-database-1 psql -X -v ON_ERROR_STOP=1 -U clary -d $Database -At -c $Sql
 if($LASTEXITCODE -ne 0){throw 'Isolated rehearsal SQL failed; inspect privately.'}
 return ($result -join "`n")
}
function Checked-Docker([string[]]$Arguments) {& $docker @Arguments;if($LASTEXITCODE -ne 0){throw 'Rehearsal Docker operation failed.'}}
$containerDump='/tmp/'+$copy+'.dump'
$oldConnection=$env:ConnectionStrings__Knowledge
try {
 Checked-Docker @('exec','-e','PGPASSWORD','clary-lab-database-1','pg_dump','-U','clary','-d','clary','-Fc','-f',$containerDump)
 Checked-Docker @('cp',('clary-lab-database-1:'+$containerDump),(Join-Path $backup 'lab.dump'))
 Checked-Docker @('cp','clary-lab-api-1:/app/uploads',(Join-Path $backup 'uploads'))
 Checked-Docker @('exec','-e','PGPASSWORD','clary-lab-database-1','createdb','-U','clary','-T','template0',$copy)
 Checked-Docker @('exec','-e','PGPASSWORD','clary-lab-database-1','pg_restore','-U','clary','-d',$copy,'--exit-on-error','--no-owner',$containerDump)
 $tables=Invoke-LabSql $copy "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename"
 $snapshots=@{};$columns=@{};$counts=@{}
 foreach($table in $tables.Split("`n")) {
  if($table -notmatch '^[a-z_]+$'){throw 'Unexpected table name'}
  $columns[$table]=Invoke-LabSql $copy "SELECT string_agg(quote_ident(column_name),',' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_schema='public' AND table_name='$table'"
  $query="SELECT count(*)||':'||md5(coalesce(string_agg(row_data::text,E'\n' ORDER BY row_data::text),'')) FROM (SELECT to_jsonb(t) row_data FROM (SELECT $($columns[$table]) FROM $table) t) x"
  $snapshots[$table]=Invoke-LabSql $copy $query
  $counts[$table]=[int](($snapshots[$table] -split ':')[0])
 }
 $snapshots|ConvertTo-Json|Set-Content (Join-Path $backup 'original-row-hashes.json')
 $fileHashes=@{}
 Get-ChildItem -LiteralPath (Join-Path $backup 'uploads') -Recurse -File | ForEach-Object {$fileHashes[$_.FullName.Substring($backup.Length+1)]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
 $fileHashes|ConvertTo-Json|Set-Content (Join-Path $backup 'file-hashes.json')
 $dangling=[int](Invoke-LabSql $copy 'SELECT count(*) FROM documents d LEFT JOIN documents target ON target.id=d.replaced_by WHERE d.replaced_by IS NOT NULL AND target.id IS NULL')
 $originalSnapshots=$snapshots.Clone()
 if($RehearseDanglingReplacementRepair) {
  $documentContentQuery="SELECT md5(coalesce(string_agg((to_jsonb(t)-'replaced_by')::text,E'\n' ORDER BY (to_jsonb(t)-'replaced_by')::text),'')) FROM (SELECT $($columns['documents']) FROM documents) t"
  $unchangedDocumentFields=Invoke-LabSql $copy $documentContentQuery
  # Explicit isolated-copy dryrun only. The SQL fails unless the exact reviewed conflict matches.
  $null=Invoke-LabSql $copy ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'repair-proposals/001-archive-dangling-document-replacement.sql')))
  if((Invoke-LabSql $copy "SELECT count(*) FROM legacy_document_replacement_archive WHERE document_id='0b1d819b-8ba0-4de2-8f2f-6a82217e6326' AND original_replaced_by='11e84017-1a5b-4b14-a807-78aa19ff2d1c'") -ne '1'){throw 'Archive verification failed'}
  if((Invoke-LabSql $copy $documentContentQuery) -cne $unchangedDocumentFields){throw 'Repair changed original document fields beyond the explicitly reviewed pointer'}
  if((Invoke-LabSql $copy "SELECT count(*) FROM documents WHERE id='0b1d819b-8ba0-4de2-8f2f-6a82217e6326' AND replaced_by IS NULL") -ne '1'){throw 'Exact proposed pointer repair failed'}
  $snapshots['documents']=Invoke-LabSql $copy "SELECT count(*)||':'||md5(coalesce(string_agg(row_data::text,E'\n' ORDER BY row_data::text),'')) FROM (SELECT to_jsonb(t) row_data FROM (SELECT $($columns['documents']) FROM documents) t) x"
 }
 $env:ConnectionStrings__Knowledge='Host=127.0.0.1;Port=5433;Database='+$copy+';Username=clary;Password='+$env:POSTGRES_PASSWORD
 & (Join-Path $PSScriptRoot 'Start-Clary.ps1') -Mode Migrate -Provider Ollama
 if($LASTEXITCODE -ne 0){throw 'Copied database migrations failed'}
 if((Invoke-LabSql $copy 'SELECT count(*) FROM schema_migrations WHERE version=8') -ne '1'){throw 'Local credential migration008 missing from isolated copy'}
 if((Invoke-LabSql $copy 'SELECT count(*) FROM local_credentials') -ne '0' -or (Invoke-LabSql $copy 'SELECT count(*) FROM local_credential_tokens') -ne '0'){throw 'Migration inferred credentials or issued tokens for legacy data'}
 foreach($table in $snapshots.Keys) {
  $query="SELECT count(*)||':'||md5(coalesce(string_agg(row_data::text,E'\n' ORDER BY row_data::text),'')) FROM (SELECT to_jsonb(t) row_data FROM (SELECT $($columns[$table]) FROM $table) t) x"
  if((Invoke-LabSql $copy $query) -cne $snapshots[$table]){throw "Preservation mismatch: $table"}
 }
 foreach($relative in $fileHashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $backup $relative) -Algorithm SHA256).Hash -cne $fileHashes[$relative]){throw 'Copied upload changed'}}
 $owned=(Invoke-LabSql $copy "SELECT coalesce(jsonb_agg(storage_path),'[]') FROM owned_files")|ConvertFrom-Json
 $missing=0
 foreach($relative in $owned) {
  if($relative -notmatch '^uploads/' -or $relative -match '(^|/)\.\.(/|$)|\\'){throw 'Unsafe owned file path'}
  if(-not (Test-Path -LiteralPath (Join-Path $backup $relative))){$missing++}
 }
 $metrics=[ordered]@{copyDatabase=$copy;backupPath=$backup;legacyTableCounts=$counts;preservedTables=$snapshots.Count;copiedFiles=$fileHashes.Count;ownedFiles=$owned.Count;missingOwnedFiles=$missing;originalDanglingReplacementLinks=$dangling;isolatedRepairRehearsed=[bool]$RehearseDanglingReplacementRepair;migrations=(Invoke-LabSql $copy "SELECT string_agg(version::text,',' ORDER BY version) FROM schema_migrations");localCredentialRows=[int](Invoke-LabSql $copy 'SELECT count(*) FROM local_credentials');localCredentialTokenRows=[int](Invoke-LabSql $copy 'SELECT count(*) FROM local_credential_tokens');documentFileLinks=[int](Invoke-LabSql $copy 'SELECT count(*) FROM document_files');ownerBindings=[int](Invoke-LabSql $copy 'SELECT count(*) FROM operator_owner');memberships=[int](Invoke-LabSql $copy 'SELECT count(*) FROM memberships');multiCompanyConflicts=[int](Invoke-LabSql $copy 'SELECT count(*) FROM (SELECT user_id FROM memberships GROUP BY user_id HAVING count(*)>1) x');enabledAdministratorConflicts=[int](Invoke-LabSql $copy "SELECT count(*) FROM (SELECT m.company_id FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.active AND u.active AND m.role='Administrator' GROUP BY m.company_id HAVING count(*)>1) x")}
 Checked-Docker @('exec','clary-lab-database-1','pg_dump','-U','clary','-d',$copy,'-Fc','-f',('/tmp/'+$copy+'-migrated.dump'))
 Checked-Docker @('cp',('clary-lab-database-1:/tmp/'+$copy+'-migrated.dump'),(Join-Path $backup 'migrated-copy.dump'))
 Checked-Docker @('exec','clary-lab-database-1','rm','-f',('/tmp/'+$copy+'-migrated.dump'))
 # Roll back the isolated copy from the original dump, including the archived pointer's original value.
 Checked-Docker @('exec','clary-lab-database-1','dropdb','-U','clary',$copy)
 Checked-Docker @('exec','clary-lab-database-1','createdb','-U','clary','-T','template0',$copy)
 Checked-Docker @('exec','clary-lab-database-1','pg_restore','-U','clary','-d',$copy,'--exit-on-error','--no-owner',$containerDump)
 foreach($table in $originalSnapshots.Keys) {
  $query="SELECT count(*)||':'||md5(coalesce(string_agg(row_data::text,E'\n' ORDER BY row_data::text),'')) FROM (SELECT to_jsonb(t) row_data FROM (SELECT $($columns[$table]) FROM $table) t) x"
  if((Invoke-LabSql $copy $query) -cne $originalSnapshots[$table]){throw 'Original backup rollback mismatch'}
 }
 $metrics['originalBackupRollbackVerified']=$true
 # Private snapshot values stay in the backup directory; the report contains counts only.
 $snapshots|ConvertTo-Json|Set-Content (Join-Path $backup 'row-hashes.json')
 $fileHashes|ConvertTo-Json|Set-Content (Join-Path $backup 'file-hashes.json')
 $metrics|ConvertTo-Json -Depth 5|Set-Content (Join-Path $backup 'metrics.json')
 $metrics|ConvertTo-Json -Depth 5
}finally {
 $env:ConnectionStrings__Knowledge=$oldConnection
 $env:PGPASSWORD=$null
 # Only generated rehearsal names may be dropped. Never DROP or migrate the actual Lab database.
 if($copy -match '^clary_rehearsal_[0-9]{8}_[0-9]{6}_[a-f0-9]{8}$') {
  & $docker exec clary-lab-database-1 dropdb -U clary --if-exists $copy
  & $docker exec clary-lab-database-1 rm -f $containerDump
 }
}
