using System;
using CommandSystem;
using LabApi.Loader.Features.Plugins;
using LabApi.Events.Handlers;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Wrappers;
using PlayerRoles;
using PlayerStatsSystem;

// Local-only quick-tier damage-path probe; never include in a production bundle.
public sealed class DamageProbe : Plugin
{
    public override string Name => "Scp181DamageProbe";
    public override string Description => "Local damage and lifecycle probe";
    public override string Author => "Local QA";
    public override Version Version => new Version(1, 0);
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
    public string Description => "Local QA: state/damage/escape/protect/cancelrole";
    public bool Execute(ArraySegment<string> args, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.PlayersManagement, out response)) return false;
        if (args.Count < 2) { response = "action player-id [value]"; return false; }
        var p = Player.Get(int.Parse(args.At(1)));
        switch (args.At(0))
        {
            case "damage": p.ReferenceHub.playerStats.DealDamage(new UniversalDamageHandler(float.Parse(args.At(2)), DeathTranslations.Falldown)); break;
            case "escape": p.ReferenceHub.roleManager.ServerSetRole(RoleTypeId.ChaosConscript, RoleChangeReason.Escaped); break;
            case "protect": DamageProbe.Protect = bool.Parse(args.At(2)); break;
            case "cancelrole": DamageProbe.CancelRole = bool.Parse(args.At(2)); break;
        }
        var effects = p.ReferenceHub.playerEffectsController;
        response = $"id={p.PlayerId} role={p.Role} hp={p.Health} ensnared={effects.GetEffect<CustomPlayerEffects.Ensnared>().Intensity} reduction={effects.GetEffect<CustomPlayerEffects.DamageReduction>().Intensity}";
        return true;
    }
}
static class ProbeArgs { public static string At(this ArraySegment<string> a, int i) => a.Array[a.Offset+i]; }
