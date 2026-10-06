param($Context, [switch]$LacunaOnly)
# Arranged roles/health; strikes and item selection use real client input.
$ErrorActionPreference = 'Stop'
$actorId = [int]$Context.Actor.id
$runEvidence = $Context.Evidence
$observations = [ordered]@{ cases = [ordered]@{} }
function Server([string]$command) {
    $reply = (Invoke-LabServer $command | Out-String).Trim()
    "> $command`n$reply" | Add-Content "$runEvidence/walkthrough.log" -Encoding utf8
    return $reply
}
function Players { @(Observe) }
function State([int]$id) { @(Players | Where-Object id -eq $id)[0] }
function WaitFor([scriptblock]$condition, [string]$message, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $condition) { return }; Start-Sleep -Milliseconds 300 } while ((Get-Date) -lt $deadline)
    throw $message
}
function Save { $observations | ConvertTo-Json -Depth 10 | Set-Content "$runEvidence/scenario.json" -Encoding utf8 }
function Spawn([string]$name, [string]$role) {
    $known = @(Players | ForEach-Object id)
    $null = Server "/dummy spawn $name"
    WaitFor { @(Players | Where-Object { $_.dummy -and $_.id -notin $known }).Count -eq 1 } 'Dummy did not spawn'
    $id = [int]@(Players | Where-Object { $_.dummy -and $_.id -notin $known })[0].id
    $null = Server "/forcerole $id $role"
    WaitFor { (State $id).role -eq $role } "Dummy did not become $role"
    $null = Server "/god $id disable"
    return $id
}
function Strike([string]$name, [int]$frames = 210) {
    $target = State $victimId
    $null = Set-LabAim -Target @{ x = $target.position.x; y = $target.camera.y - 0.35; z = $target.position.z }
    $null = Invoke-LabInput @{ id = $name; frames = $frames; inputFrames = 30; keys = @(323); capture = $true; audio = $true; expectAudio = $true }
    $observations.cases[$name] = [ordered]@{
        victim = State $victimId; spectator = State $spectatorId
        knife = Server "/damageprobe knifestate $actorId"
        eligibility = Server "/damageprobe knifestate $spectatorId"
    }
    Save
}

if ((Server '/forcestart') -notmatch 'Forced round start') { throw 'Round did not start' }
WaitFor { (Server '/roundtime') -match 'Round time:' } 'Round not ready'
Start-Sleep -Seconds 3
foreach ($command in '/decontamination disable', '/wave forcepause NTF true', '/wave forcepause Chaos true', '/reinforcements auto off', '/scp181 clear') { $null = Server $command }
if (!$LacunaOnly) {
$null = Server "/forcerole $actorId NtfSergeant"
WaitFor { (State $actorId).role -eq 'NtfSergeant' } 'Attacker not NTF'
$null = Server "/god $actorId enable"
foreach ($p in Players | Where-Object { $_.id -ne $actorId }) { $null = Server "/damageprobe knifeeligible $($p.id) false" }
if (@($Context.Actors).Count -ne 2) { throw 'Walkthrough needs --pair for a real victim client' }
$victimId = [int]$Context.Actors[1].id
$null = Server "/forcerole $victimId ClassD"
WaitFor { (State $victimId).role -eq 'ClassD' } 'Knife victim not Class-D'
$null = Server "/god $victimId disable"
$spectatorId = Spawn 'KnifeSpectator' 'Spectator'
WaitFor { (Server "/damageprobe knifestate $spectatorId") -match 'eligible=True' } 'No eligible spectator'
$deadline = (Get-Date).AddSeconds(100)
do {
    $reply = Server "/scp181 set $victimId"
    if ($reply -match '已将玩家') { break }
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)
if ($reply -notmatch '已将玩家') { throw "SCP-181 assignment failed: $reply" }
$null = Server "/damageprobe escape $victimId"
WaitFor { (State $victimId).role -eq 'ChaosConscript' } 'Knife target did not escape to Chaos'

# ItemType.SCP1509 is 68 in the pinned server. Give through native RA.
$null = Server "/give $actorId 68"
WaitFor { (State $actorId).items -contains 'SCP1509' } 'Knife not granted'
$slot = [Array]::IndexOf(@((State $actorId).items), 'SCP1509') + 1
$null = Invoke-LabInventory -Slot $slot -Expect SCP1509
Start-Sleep -Seconds 1
$me = State $actorId
$null = Server ('labplacecheck {0} {1} {2} ChaosConscript' -f $me.position.x, $me.position.y, ($me.position.z + 1.2))
$null = Server "/damageprobe place $victimId $actorId 0 1.2"
Start-Sleep -Seconds 1
$observations.before = [ordered]@{ victim = State $victimId; spectator = State $spectatorId; knife = Server "/damageprobe knifestate $actorId" }
Save

Strike 'nonlethal'
if ((State $victimId).health -ge 100 -or (State $victimId).health -le 1) { throw 'Nonlethal knife strike did not land' }
if ((State $spectatorId).role -ne 'Spectator') { throw 'Nonlethal hit resurrected someone' }

$null = Server "/damageprobe sethp $victimId 10"
Strike 'last-stand'
if ((State $victimId).role -ne 'ChaosConscript' -or [Math]::Abs((State $victimId).health - 1) -gt 0.01) { throw 'Last stand did not leave 1 HP' }
if ((State $spectatorId).role -ne 'Spectator') { throw 'Survived knife hit resurrected a teammate' }
if ((Server "/damageprobe knifestate $spectatorId") -notmatch 'eligible=True') { throw 'Blocked resurrection consumed spectator eligibility' }
if ((Server "/damageprobe knifestate $actorId") -notmatch 'cooldownLeft=0.000') { throw 'Blocked resurrection consumed knife cooldown' }

# The recorded tail exceeds last-stand immunity; the same knife must now kill and resurrect.
Strike 'confirmed-death'
if ((State $victimId).role -ne 'Spectator') { throw 'Last-stand charge was not spent exactly once' }
if ((State $spectatorId).role -ne 'NtfPrivate') { throw 'Confirmed knife death did not resurrect the spectator' }
if ((Server "/damageprobe knifestate $actorId") -match 'cooldownLeft=0.000') { throw 'Successful resurrection did not start cooldown' }
# Both O-11 guns must resolve last stand before creating their first death effect.
$null = Server "/forcerole $actorId NtfSergeant"
WaitFor { (State $actorId).role -eq 'NtfSergeant' } 'O-11 gun fixture attacker not NTF'
$null = Server "/god $actorId enable"
foreach ($weapon in @(
    @{ item = 'o11-redaction-fsp9'; type = 'GunFSP9'; toy = 'redactionRigs' },
    @{ item = 'o11-echo-com18'; type = 'GunCOM18'; toy = 'echoCores' }
)) {
    $null = Server "/forcerole $victimId ClassD"
    WaitFor { (State $victimId).role -eq 'ClassD' } 'O-11 target not Class-D'
    $null = Server "/god $victimId disable"
    if ((Server "/scp181 set $victimId") -notmatch '已将玩家') { throw 'O-11 SCP-181 assignment failed' }
    $null = Server "/damageprobe escape $victimId"
    WaitFor { (State $victimId).role -eq 'ChaosConscript' } 'O-11 target did not escape to Chaos'
    $null = Server "/damageprobe sethp $victimId 10"
    $null = Server "/reinforcements items $($weapon.item) $actorId"
    WaitFor { (State $actorId).items -contains $weapon.type } 'O-11 gun not granted'
    $slot = [Array]::IndexOf(@((State $actorId).items), $weapon.type) + 1
    $null = Invoke-LabInventory -Slot $slot -Expect $weapon.type
    Start-Sleep -Seconds 1
    $null = Server "/damageprobe place $victimId $actorId 0 3"
    Start-Sleep -Seconds 1
    $beforeToys = Server "/damageprobe o11state $actorId"
    if ($beforeToys -notmatch "$($weapon.toy)=0") { throw "Death effect pool already exists: $beforeToys" }
    Strike "$($weapon.item)-last-stand"
    if ((State $victimId).role -ne 'ChaosConscript' -or [Math]::Abs((State $victimId).health - 1) -gt 0.01) { throw 'O-11 last stand did not leave 1 HP' }
    $afterToys = Server "/damageprobe o11state $actorId"
    $observations.cases["$($weapon.item)-last-stand"].toys = $afterToys
    Save
    if ($afterToys -notmatch "$($weapon.toy)=0") { throw 'O-11 played a death effect on a survivor' }
    Strike "$($weapon.item)-confirmed-death"
    if ((State $victimId).role -ne 'Spectator') { throw 'O-11 confirmed lethal hit did not kill' }
    $afterToys = Server "/damageprobe o11state $actorId"
    $observations.cases["$($weapon.item)-confirmed-death"].toys = $afterToys
    Save
    if ($afterToys -notmatch "$($weapon.toy)=[1-9]") { throw 'O-11 confirmed death did not create its effect' }
}
}

if (@($Context.Actors).Count -ne 2) { throw 'Lacuna walkthrough needs --pair for victim-view recording' }
$victimId = [int]$Context.Actors[1].id
$victimClient = [int]$Context.ClientIds[1]
$spectatorId = $victimId
$null = Server "/forcerole $actorId Spectator"
Start-Sleep -Seconds 1
if ((Server "/reinforcements sub lacuna operative $actorId") -notmatch 'Lacuna|lacuna|operative|分配|缺页') { throw 'Lacuna assignment failed' }
WaitFor { (State $actorId).role -eq 'ChaosRifleman' } 'Attacker not Lacuna'
$null = Server "/god $actorId disable"
$null = Server "/forcerole $victimId ClassD"
WaitFor { (State $victimId).role -eq 'ClassD' } 'Lacuna target not Class-D'
$null = Server "/god $victimId disable"
if ((Server "/scp181 set $victimId") -notmatch '已将玩家') { throw 'Lacuna SCP-181 assignment failed' }
# Escaped SCP-181 is the hostile native NTF fixture; retention is a declared test configuration.
$null = Server "/damageprobe escapentf $victimId"
WaitFor { (State $victimId).role -eq 'NtfPrivate' } 'Lacuna target did not escape to NTF'
if ((Server '/scp181 status') -notmatch "ID: $victimId\b") { throw 'Escaped target did not retain SCP-181' }
# The operative kit has one firearm; use its native primary-weapon hotkey.
$null = Invoke-LabInput @{ frames = 90; inputFrames = 2; keys = @(49) }
WaitFor { (State $actorId).held -eq 'GunShotgun' } 'Lacuna shotgun not selected'
$null = Server "/damageprobe place $victimId $actorId 0 3"
Start-Sleep -Seconds 4
$null = Server "/damageprobe sethp $victimId 10"

function VictimCapture([string]$name, [int]$frames) {
    $folder = "$runEvidence/mod-input-$victimClient"
    $request = @{ id = $name; frames = $frames; capture = $true; audio = $true }
    $request | ConvertTo-Json | Set-Content "$folder/command.tmp" -Encoding utf8
    [IO.File]::Move("$folder/command.tmp", "$folder/command.json", $true)
    return @{ folder = $folder; id = $name }
}
function AwaitVictim($handle) {
    WaitFor {
        try { $done = Get-Content "$($handle.folder)/result.json" -Raw | ConvertFrom-Json } catch { return $false }
        if ($done.id -ne $handle.id) { return $false }
        if ($done.error) { throw $done.error }
        return $true
    } 'Victim recording did not finish' 60
}
$victimCapture = VictimCapture 'lacuna-survived-victim' 420
Strike 'lacuna-last-stand'
AwaitVictim $victimCapture
if ((State $victimId).role -ne 'NtfPrivate' -or [Math]::Abs((State $victimId).health - 1) -gt 0.01 -or (State $victimId).godMode) { throw 'Lacuna started recall or killed the last-stand survivor' }
$observations.cases['lacuna-last-stand'].victimAfterRecallWindow = State $victimId
Save
$victimCapture = VictimCapture 'lacuna-confirmed-death-victim' 720
Strike 'lacuna-confirmed-death' 600
AwaitVictim $victimCapture
if ((State $victimId).role -ne 'Spectator' -or (State $victimId).godMode) { throw 'Confirmed Lacuna death did not finish and clean up' }
$observations.verified = $true
Save
