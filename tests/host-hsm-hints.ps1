param($Context)
# SCP-181 HSM hints through one client: role card, last stand, item copy, escape recolour and
# removal on the client itself, then the attacker's dodge countdown against a dummy SCP-181.
# Package Scp181.dll, HsmAdapter.dll, Scp181DamageProbe.dll and tests/host-hsm-hints.config.yml.
# Setup uses RA and the local-only probe; the pickup and the shots are native client input.
$ErrorActionPreference = 'Stop'
$ev = $Context.Evidence
$id = [int]$Context.Actor.id
$clientPid = [int]$Context.ClientId
$result = [ordered]@{ clips = [ordered]@{}; screenshots = [ordered]@{} }

function Note([string]$text) { "[$((Get-Date).ToString('HH:mm:ss.fff'))] $text" | Add-Content "$ev\walkthrough.log" -Encoding utf8 }
function Server([string]$command) { $reply = (Invoke-LabServer $command | Out-String).Trim(); Note "> $command"; Note $reply; $reply }
function Players { @((Get-LabSnapshot).players) }
function PlayerState([int]$playerId) { @(Players | Where-Object id -eq $playerId)[0] }
function WaitFor([scriptblock]$condition, [string]$message, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { $value = & $condition; if ($value) { return $value }; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $deadline)
    throw $message
}
function Save { $result | ConvertTo-Json -Depth 8 | Set-Content "$ev\scenario.json" -Encoding utf8 }
# A capture submitted without waiting, so a server-side trigger lands inside the recording.
function Submit([hashtable]$request) {
    $folder = "$ev\mod-input-$clientPid"
    if (!(Test-Path "$folder\ready.json")) { throw "Adapter for client $clientPid not ready" }
    $request | ConvertTo-Json -Depth 6 | Set-Content "$folder\command.tmp" -Encoding utf8
    for ($i = 0; ; $i++) { try { [IO.File]::Move("$folder\command.tmp", "$folder\command.json", $true); break } catch { if ($i -ge 19) { throw }; Start-Sleep -Milliseconds 50 } }
    Note "SUBMIT $($request.id) frames=$($request.frames)"
    [pscustomobject]@{ id = $request.id; folder = $folder }
}
function Await($handle, [int]$seconds = 180) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        Start-Sleep -Milliseconds 100
        try { $done = Get-Content "$($handle.folder)\result.json" -Raw -Encoding utf8 -ErrorAction Stop | ConvertFrom-Json } catch { continue }
        if ($done.id -eq $handle.id) { if ($done.error) { throw "$($handle.id): $($done.error)" }; Note "DONE $($handle.id) fps=$($done.fps)"; return $done }
    } while ((Get-Date) -lt $deadline)
    throw "Input request timed out: $($handle.id)"
}
# ReinforcementsSystem blocks assignment while its initial role selection is pending (up to 60 s).
function Assign([int]$playerId) {
    $deadline = (Get-Date).AddSeconds(100)
    do {
        $reply = Server "/scp181 set $playerId"
        if ($reply -match '已将玩家') { return }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $deadline)
    throw "scp181 set $playerId was not accepted: $reply"
}

# --- Round: started, hazards paused, runner round lock kept ---------------------------------------
$start = Server '/forcestart'
if ($start -notmatch 'Forced round start') { throw "Round did not start: $start" }
WaitFor { (Server '/roundtime') -match 'Round time:' } 'Round time did not start' 20 | Out-Null
Start-Sleep -Seconds 3
foreach ($command in '/decontamination disable', '/wave forcepause NTF true', '/wave forcepause Chaos true', '/reinforcements auto off') { $null = Server $command }

# --- Role card: persistent, orange while Class-D ----------------------------------------------------
# A fresh Class-D body drops any round-start identity another plugin may track on the actor.
$null = Server "/forcerole $id ClassD"
$null = WaitFor { (PlayerState $id).role -eq 'ClassD' } 'Actor did not respawn as Class-D'
Assign $id
$me = WaitFor { $p = PlayerState $id; if ($p.role -eq 'ClassD') { $p } } 'Actor did not become Class-D'
if ((Server '/scp181 status') -notmatch "ID: $id\b") { throw 'scp181 status does not list the actor' }
Start-Sleep -Seconds 2
$result.screenshots.roleCard = Invoke-LabScreenshot -Name 'role-card-classd'

# --- Last stand: a lethal fixture hit leaves 1 HP and shows the green notice for 3 s ----------------
$null = Server "/god $id disable"
$survived = $false
for ($attempt = 1; $attempt -le 8 -and -not $survived; $attempt++) {
    $clip = Submit @{ id = "last-stand-$attempt"; frames = 330; capture = $true; audio = $true }
    Start-Sleep -Milliseconds 1500
    $null = Server "/damageprobe damage $id 1000"
    $null = Await $clip
    $me = PlayerState $id
    if ($me.role -ne 'ClassD') { throw "Actor did not survive the lethal hit: $($me | ConvertTo-Json -Compress)" }
    # A dodged hit (dodge_chance 0.5) leaves health unchanged; retry until the last stand fires.
    if ([double]$me.health -le 1.01) { $survived = $true; $result.clips.lastStand = $clip.id }
}
if (-not $survived) { throw 'Last stand never fired' }
$null = Server "/god $id enable"

# --- Item copy: native pickup of a Medkit adds the original and one copy ---------------------------
$null = Set-LabLook -Yaw ([double](PlayerState $id).look.yaw) -Pitch -70
$before = @((PlayerState $id).items | Where-Object { $_ -eq 'Medkit' }).Count
$drop = Server "/damageprobe pickup $id Medkit"
if ($drop -notmatch 'pickup serial=') { throw "Pickup fixture failed: $drop" }
Start-Sleep -Seconds 1
$null = Invoke-LabInput @{ id = 'copy-pickup'; frames = 330; inputFrames = 75; keys = @(101); capture = $true; audio = $true }
$result.clips.copy = 'copy-pickup'
$after = @((PlayerState $id).items | Where-Object { $_ -eq 'Medkit' }).Count
if ($after -lt $before + 2) { throw "Pickup did not add a copy: Medkit $before -> $after" }

# --- Escape: the card is re-shown in the Chaos colour -----------------------------------------------
$null = Server "/damageprobe escape $id"
$null = WaitFor { $p = PlayerState $id; if ($p.role -eq 'ChaosConscript') { $p } } 'Escape did not complete'
if ((Server '/scp181 status') -notmatch "ID: $id\b") { throw 'Escape removed SCP-181' }
Start-Sleep -Seconds 2
$result.screenshots.roleCardChaos = Invoke-LabScreenshot -Name 'role-card-chaos'

# --- Removal: scp181 clear takes the card down --------------------------------------------------------
if ((Server '/scp181 clear') -notmatch '已清除') { throw 'scp181 clear failed' }
if ((Server '/scp181 status') -notmatch '当前没有') { throw 'SCP-181 still listed after clear' }
Start-Sleep -Seconds 2
$result.screenshots.cleared = Invoke-LabScreenshot -Name 'role-card-cleared'
Save

# --- Dodge: the client (NTF) shoots a dummy SCP-181 and sees the 5 s countdown -----------------------
$null = Server "/forcerole $id NtfSergeant"
$null = WaitFor { $p = PlayerState $id; if ($p.role -eq 'NtfSergeant' -and $p.items -contains 'GunE11SR') { $p } } 'Actor did not spawn as NTF Sergeant'
$null = Server "/god $id enable"
$known = @(Players | Where-Object dummy | ForEach-Object id)
$null = Server '/dummy spawn Scp181Target'
$dummy = [int](WaitFor { @(Players | Where-Object { $_.dummy -and $_.id -notin $known })[0].id } 'Dummy was not spawned')
$null = Server "/forcerole $dummy ClassD"
$null = WaitFor { (PlayerState $dummy).role -eq 'ClassD' } 'Dummy did not become Class-D'
Assign $dummy
$me = PlayerState $id
$yaw = [double]$me.look.yaw * [Math]::PI / 180
$dx = '{0:0.00}' -f (3 * [Math]::Sin($yaw)); $dz = '{0:0.00}' -f (3 * [Math]::Cos($yaw))
$null = Server ('labplacecheck {0:0.00} {1:0.00} {2:0.00} ClassD' -f ($me.position.x + [double]$dx), $me.position.y, ($me.position.z + [double]$dz))
$null = Server "/damageprobe place $dummy $id $dx $dz"
Start-Sleep -Seconds 1
$null = Invoke-LabInput @{ id = 'equip-e11'; frames = 60; inputFrames = 2; keys = @(49) }
Start-Sleep -Milliseconds 500
if ((PlayerState $id).held -ne 'GunE11SR') {
    $slots = @((PlayerState $id).items).Count
    foreach ($slot in 1..([Math]::Min(8, $slots))) {
        try { $null = Invoke-LabInventory -Slot $slot -Expect GunE11SR; break } catch { Note "wheel slot $slot miss: $($_.Exception.Message)" }
    }
}
$null = WaitFor { (PlayerState $id).held -eq 'GunE11SR' } 'E11 was not equipped' 10
Start-Sleep -Seconds 1
$target = PlayerState $dummy
$null = Set-LabAim -Target @{ x = $target.position.x; y = $target.camera.y - 0.35; z = $target.position.z }
$result.dodgeTarget = @{ dummy = $dummy; before = (PlayerState $dummy).health }
# Two short bursts: each landed hit is dodged with probability 0.5 and restarts the countdown.
foreach ($burst in 1, 2) {
    $null = Invoke-LabInput @{ id = "dodge-$burst"; frames = 420; inputFrames = 20; keys = @(323); capture = $true; audio = $true; expectAudio = $true }
    $result.clips["dodge$burst"] = "dodge-$burst"
    $result.dodgeTarget["after$burst"] = (PlayerState $dummy).health
}
if ((PlayerState $dummy).role -ne 'ClassD') { throw 'Dummy SCP-181 did not survive the bursts' }

$null = Server '/scp181 clear'
$result.final = PlayerState $id
Save
