param($Context)
# Quick-tier hand-loss walkthrough: SCP-181 must bleed out like any Class-D. Native RA enables the
# same SeveredHands effect as SCP-330; this isolates its ticking damage, not bowl input.
$ErrorActionPreference = 'Stop'
$actorId = [int]$Context.Actor.id
$runEvidence = $Context.Evidence
$observations = [ordered]@{ trigger = 'Native RA SeveredHands effect, not bowl interaction'; cases = [ordered]@{} }
function Server([string]$command) {
    $reply = (Invoke-LabServer $command | Out-String).Trim()
    "> $command`n$reply" | Add-Content "$runEvidence/walkthrough.log" -Encoding utf8
    return $reply
}
function State { @(Observe | Where-Object id -eq $actorId)[0] }
function Save { $observations | ConvertTo-Json -Depth 10 | Set-Content "$runEvidence/scenario.json" -Encoding utf8 }
function ReadyClassD {
    $null = Server "/forcerole $actorId ClassD"
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 300; $s = State } while ($s.role -ne 'ClassD' -and (Get-Date) -lt $deadline)
    if ($s.role -ne 'ClassD') { throw 'Class-D fixture did not spawn' }
    $null = Server "/god $actorId disable"
    $null = Server "/damageprobe sethp $actorId 100"
    $s = State
    if ($s.godMode -or $s.health -ne 100) { throw 'Fixture must have 100 HP without god mode' }
    $null = Set-LabLook -Yaw $s.look.yaw -Pitch 0
}
function Hands([string]$name) {
    $reply = Server "/effect SeveredHands 1 120 $actorId"
    if ($reply -notmatch 'affected 1 player') { throw "SeveredHands fixture failed: $reply" }
    $probe = Server "/damageprobe state $actorId"
    if ($probe -notmatch 'severed=True') { throw "Native effect did not enable: $probe" }
    $observations.cases[$name] = [ordered]@{ before = State; effectBefore = $probe }
    Save
}

if ((Server '/forcestart') -notmatch 'Forced round start') { throw 'Round did not start' }
$deadline = (Get-Date).AddSeconds(20)
do { Start-Sleep -Milliseconds 500; $started = (Server '/roundtime') -match 'Round time:' } while (!$started -and (Get-Date) -lt $deadline)
if (!$started) { throw 'Round not ready' }
Start-Sleep -Seconds 3
foreach ($command in '/decontamination disable', '/wave forcepause NTF true', '/wave forcepause Chaos true', '/reinforcements auto off', '/scp181 clear') { $null = Server $command }

# Control: the unmodified native effect must actually be lethal in this deployment.
ReadyClassD
Hands 'ordinaryClassD'
$null = Invoke-LabInput @{ id = 'ordinary-severed-hands'; frames = 1800; capture = $true; audio = $true }
$observations.cases.ordinaryClassD.after = State
Save
if ((State).role -ne 'Spectator') { throw 'Control did not die within the recorded 30-second interval' }

ReadyClassD
$deadline = (Get-Date).AddSeconds(100)
do {
    $reply = Server "/scp181 set $actorId"
    if ($reply -match '已将玩家') { break }
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)
if ($reply -notmatch '已将玩家' -or (Server '/scp181 status') -notmatch "ID: $actorId\b") { throw 'SCP-181 assignment not ready' }
Hands 'scp181'
# Last stand catches the first lethal tick (1 HP, short immunity); the next ticks must kill.
$null = Invoke-LabInput @{ id = 'scp181-severed-hands'; frames = 2700; capture = $true; audio = $true }
$observations.cases.scp181.after = State
$observations.cases.scp181.statusAfter = Server '/scp181 status'
Save
if ((State).role -ne 'Spectator') { throw 'SCP-181 survived severed hands within the recorded 45-second interval' }
$observations.verified = $true
Save
