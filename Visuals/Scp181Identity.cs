using System.Collections.Generic;
using LabApi.Features.Wrappers;
using Log = LabApi.Features.Console.Logger;

namespace Scp181.Visuals;

/// <summary>
/// Look-at panel identity, following ReinforcementsSystem's <c>ReinforcementIdentityService</c>: one
/// native <c>CustomInfo</c> line under the nickname. The group badge is left to PlayerBadge and RA groups.
/// </summary>
internal static class Scp181Identity
{
    // NicknameSync.ValidateCustomInfo only accepts six-digit hex from Misc.AcceptedColours; EE7600 is
    // the orange entry. Square brackets are rejected by Misc.PlayerCustomInfoRegex.
    private const string InfoLine = "<b><color=#EE7600>SCP-181</color></b>";
    private static readonly Dictionary<Player, (string CustomInfo, bool CustomInfoVisible)> Saved = new();

    public static void Apply(Player player)
    {
        if (!Player.ValidateCustomInfo(InfoLine, out string rejection))
        {
            Log.Warn($"[Scp181] Custom info rejected for {player.Nickname}: {rejection}");
            return;
        }

        // A re-apply (escape, reassignment) must not capture this plugin's own line as the original.
        if (!Saved.ContainsKey(player))
            Saved[player] = (player.CustomInfo, (player.InfoArea & PlayerInfoArea.CustomInfo) != 0);

        player.CustomInfo = InfoLine;
        player.InfoArea |= PlayerInfoArea.CustomInfo;
    }

    public static void Remove(Player player)
    {
        if (!Saved.TryGetValue(player, out var original))
            return;
        Saved.Remove(player);
        if (player.IsDestroyed)
            return;

        // Do not erase a line another plugin has written in the meantime.
        if (player.CustomInfo != InfoLine)
            return;

        player.CustomInfo = original.CustomInfo ?? string.Empty;
        if (!original.CustomInfoVisible)
            player.InfoArea &= ~PlayerInfoArea.CustomInfo;
    }

    public static void Clear()
    {
        foreach (Player player in new List<Player>(Saved.Keys))
            Remove(player);
    }
}
