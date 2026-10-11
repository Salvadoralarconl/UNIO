$ErrorActionPreference='Stop'
$cases=@(
 @{Model='TM-T88IV';Question='My Epson TM-T88IV ERROR light is steady and PAPER OUT is off. Why will it not print?'},
 @{Model='TM-T88V';Question='My Epson TM-T88V Error LED is flashing. What should I check?'},
 @{Model='TM-T20II';Question='My Epson TM-T20II Paper LED is on. What should I check?'},
 @{Model='TM-T20III';Question='How do I run a self-test on my Epson TM-T20III?'},
 @{Model='TM-m30';Question='My original Epson TM-m30 error light is flashing. What should I do?'}
)
$results=@()
foreach($case in $cases){
 $a=Invoke-RestMethod 'http://127.0.0.1:5081/api/questions' -Method Post -ContentType 'application/json' -Body (@{question=$case.Question}|ConvertTo-Json) -TimeoutSec 240
 if($a.status -ne 'supported' -or @($a.steps).Count -eq 0){throw ('No supported guidance for '+$case.Model+': '+$a.status)}
 foreach($step in $a.steps){foreach($c in $step.citations){
  $p=Invoke-RestMethod ('http://127.0.0.1:5081/api/passages/'+$c.passageId)
  if(-not $p.title.StartsWith('Epson '+$case.Model+' ') -or -not $p.text.Contains($c.quote,[StringComparison]::Ordinal)){throw 'Incorrect model or citation.'}
 }}
 $results+=@{question=$case.Question;answer=$a}
 Write-Output ($case.Model+': supported, model and exact citations verified')
}
$ambiguous=Invoke-RestMethod 'http://127.0.0.1:5081/api/questions' -Method Post -ContentType 'application/json' -Body '{"question":"My Epson printer has an error light"}' -TimeoutSec 240
if($ambiguous.status -ne 'clarify' -or @($ambiguous.steps).Count -ne 0){throw 'Missing model did not request clarification.'}
Write-Output 'Unspecified Epson model: clarification, no fixes'
$results|ConvertTo-Json -Depth 12|Set-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\epson-evaluation.json')
