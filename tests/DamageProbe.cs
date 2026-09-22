using System;
using CommandSystem;
using LabApi.Loader.Features.Plugins;
using LabApi.Events.Handlers;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Wrappers;
using Mirror;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp049;
using PlayerRoles.PlayableScps.Scp106;
using PlayerStatsSystem;
using RelativePositioning;
using UnityEngine;
using Utils.Networking;

// Local-only quick-tier damage-path probe; never include in a production bundle.
public sealed class DamageProbe : Plugin
{
    public override string Name => "Scp181DamageProbe";
    public override string Description => "Local damage and lifecycle probe";
    public override string Author => "Local QA";
    public override Version Version => new Version(1, 1);
    public override Version RequiredApiVersion => new Version(1, 1);
    public static bool Protect, CancelRole;
    public override void Enable() { PlayerEvents.Hurting += Hurt; PlayerEvents.ChangingRole += Role; }
    public override void Disable() { PlayerEvents.Hurting -= Hurt; PlayerEvents.ChangingRole -= Role; }
    private void Hurt(PlayerHurtingEventArgs e) { if (Protect) e.IsAllowed = false; }
    private void Role(PlayerChangingRoleEventArgs e) { if (CancelRole) e.IsAllowed = false; }
}
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ProbeCommand : ICommand
{
    public string Command => "damageprobe";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Local QA: state/damage/sethp/escape/protect/cancelrole/place/attack106/attack049";
    public bool Execute(ArraySegment<string> args, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.PlayersManagement, out response)) return false;
        if (args.Count < 2) { response = "action player-id [value]"; return false; }
        var p = Player.Get(int.Parse(args.At(1)));
        switch (args.At(0))
        {
            case "damage": p.ReferenceHub.playerStats.DealDamage(new UniversalDamageHandler(float.Parse(args.At(2)), DeathTranslations.Falldown)); break;
            case "sethp": p.Health = float.Parse(args.At(2)); break;
            case "escape": p.ReferenceHub.roleManager.ServerSetRole(RoleTypeId.ChaosConscript, RoleChangeReason.Escaped); break;
            case "protect": DamageProbe.Protect = bool.Parse(args.At(2)); break;
            case "cancelrole": DamageProbe.CancelRole = bool.Parse(args.At(2)); break;
            // place <scp id> <target id> <dx> <dz>: stand the SCP next to the target, facing it.
            case "place":
            {
                var t = Player.Get(int.Parse(args.At(2)));
                var offset = new Vector3(float.Parse(args.At(3)), 0f, float.Parse(args.At(4)));
                p.Position = t.Position + offset;
                p.LookRotation = new Vector2(0f, Quaternion.LookRotation(-offset).eulerAngles.y);
                break;
            }
            // attack106 <scp id> <target id>: feed Scp106Attack the same command payload the client sends.
            case "attack106":
            {
                var t = Player.Get(int.Parse(args.At(2)));
                if (p.RoleBase is not Scp106Role role || !role.SubroutineModule.TryGetSubroutine<Scp106Attack>(out var attack)) { response = "not SCP-106"; return false; }
                var w = NetworkWriterPool.Get();
                w.WriteReferenceHub(t.ReferenceHub);
                w.WriteRelativePosition(new RelativePosition(t.Position));
                w.WriteQuaternion(Quaternion.LookRotation(t.Position - p.Position));
                w.WriteRelativePosition(new RelativePosition(p.Position));
                attack.ServerProcessCmd(new NetworkReader(w.ToArraySegment()));
                NetworkWriterPool.Return(w);
                break;
            }
            // attack049 <scp id> <target id>: same for Scp049AttackAbility.
            case "attack049":
            {
                var t = Player.Get(int.Parse(args.At(2)));
                if (p.RoleBase is not Scp049Role role || !role.SubroutineModule.TryGetSubroutine<Scp049AttackAbility>(out var attack)) { response = "not SCP-049"; return false; }
                var w = NetworkWriterPool.Get();
                w.WriteReferenceHub(t.ReferenceHub);
                attack.ServerProcessCmd(new NetworkReader(w.ToArraySegment()));
                NetworkWriterPool.Return(w);
                break;
            }
        }
        var effects = p.ReferenceHub.playerEffectsController;
        string room = p.Room?.Name.ToString() ?? "none";
        response = $"id={p.PlayerId} role={p.Role} hp={p.Health:F1} room={room} ensnared={effects.GetEffect<CustomPlayerEffects.Ensnared>().Intensity} reduction={effects.GetEffect<CustomPlayerEffects.DamageReduction>().Intensity}"
            + $" corroding={effects.GetEffect<CustomPlayerEffects.Corroding>().IsEnabled} pocket={effects.GetEffect<CustomPlayerEffects.PocketCorroding>().IsEnabled} cardiac={effects.GetEffect<CustomPlayerEffects.CardiacArrest>().IsEnabled}"
            + $" traumatized={effects.GetEffect<CustomPlayerEffects.Traumatized>().IsEnabled} bleeding={effects.GetEffect<CustomPlayerEffects.Bleeding>().IsEnabled}";
        return true;
    }
}
static class ProbeArgs { public static string At(this ArraySegment<string> a, int i) => a.Array[a.Offset+i]; }
