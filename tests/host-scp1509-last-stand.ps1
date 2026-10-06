param($Context)
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
function Strike([string]$name) {
    $target = State $victimId
    $null = Set-LabAim -Target @{ x = $target.position.x; y = $target.camera.y - 0.35; z = $target.position.z }
    $null = Invoke-LabInput @{ id = $name; frames = 210; inputFrames = 2; keys = @(323); capture = $true; audio = $true; expectAudio = $true }
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
$null = Server "/forcerole $actorId ChaosConscript"
WaitFor { (State $actorId).role -eq 'ChaosConscript' } 'Attacker not Chaos'
$null = Server "/god $actorId enable"
foreach ($p in Players | Where-Object { $_.id -ne $actorId }) { $null = Server "/damageprobe knifeeligible $($p.id) false" }
$victimId = Spawn 'Scp181KnifeTarget' 'ClassD'
$spectatorId = Spawn 'KnifeSpectator' 'Spectator'
WaitFor { (Server "/damageprobe knifestate $spectatorId") -match 'eligible=True' } 'No eligible spectator'
$deadline = (Get-Date).AddSeconds(100)
do {
    $reply = Server "/scp181 set $victimId"
    if ($reply -match '已将玩家') { break }
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)
if ($reply -notmatch '已将玩家') { throw "SCP-181 assignment failed: $reply" }

# ItemType.SCP1509 is 68 in the pinned server. Give through native RA.
$null = Server "/give $actorId 68"
WaitFor { (State $actorId).items -contains 'SCP1509' } 'Knife not granted'
$slot = [Array]::IndexOf(@((State $actorId).items), 'SCP1509') + 1
$null = Invoke-LabInventory -Slot $slot -Expect SCP1509
Start-Sleep -Seconds 1
$me = State $actorId
$null = Server ('labplacecheck {0} {1} {2} ClassD' -f $me.position.x, $me.position.y, ($me.position.z + 1.2))
$null = Server "/damageprobe place $victimId $actorId 0 1.2"
Start-Sleep -Seconds 1
$observations.before = [ordered]@{ victim = State $victimId; spectator = State $spectatorId; knife = Server "/damageprobe knifestate $actorId" }
Save

Strike 'nonlethal'
if ((State $victimId).health -ge 100 -or (State $victimId).health -le 1) { throw 'Nonlethal knife strike did not land' }
if ((State $spectatorId).role -ne 'Spectator') { throw 'Nonlethal hit resurrected someone' }

$null = Server "/damageprobe sethp $victimId 10"
Strike 'last-stand'
if ((State $victimId).role -ne 'ClassD' -or [Math]::Abs((State $victimId).health - 1) -gt 0.01) { throw 'Last stand did not leave 1 HP' }
if ((State $spectatorId).role -ne 'Spectator') { throw 'Survived knife hit resurrected a teammate' }
if ((Server "/damageprobe knifestate $spectatorId") -notmatch 'eligible=True') { throw 'Blocked resurrection consumed spectator eligibility' }
if ((Server "/damageprobe knifestate $actorId") -notmatch 'cooldownLeft=0.000') { throw 'Blocked resurrection consumed knife cooldown' }

# The recorded tail exceeds last-stand immunity; the same knife must now kill and resurrect.
Strike 'confirmed-death'
if ((State $victimId).role -ne 'Spectator') { throw 'Last-stand charge was not spent exactly once' }
if ((State $spectatorId).role -ne 'ChaosConscript') { throw 'Confirmed knife death did not resurrect the spectator' }
if ((Server "/damageprobe knifestate $actorId") -match 'cooldownLeft=0.000') { throw 'Successful resurrection did not start cooldown' }
$observations.verified = $true
Save
