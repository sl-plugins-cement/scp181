using System.Collections.Generic;
using LabApi.Features.Wrappers;

namespace Scp181.Visuals;

/// <summary>The native badge appears both above the player and in the N player list.</summary>
internal static class Scp181Identity
{
    private const string BadgeText = "SCP-181";
    private const string BadgeColor = "orange";
    private static readonly Dictionary<Player, (string Text, string Color, bool BadgeVisible)> Saved = new();

    public static void Apply(Player player)
    {
        if (!Saved.ContainsKey(player))
            Saved[player] = (player.GroupName, player.GroupColor, (player.InfoArea & PlayerInfoArea.Badge) != 0);

        // Presentation only: never assign a UserGroup or change RA permissions.
        player.GroupColor = BadgeColor;
        player.GroupName = BadgeText;
        player.InfoArea |= PlayerInfoArea.Badge;
    }

    public static void Remove(Player player)
    {
        if (!Saved.TryGetValue(player, out var original))
            return;
        Saved.Remove(player);
        if (player.IsDestroyed)
            return;

        // Do not erase a badge another plugin/admin has replaced in the meantime.
        if (player.GroupName == BadgeText && player.GroupColor == BadgeColor)
        {
            player.GroupName = original.Text;
            player.GroupColor = original.Color;
            if (!original.BadgeVisible)
                player.InfoArea &= ~PlayerInfoArea.Badge;
        }
    }

    public static void Clear()
    {
        foreach (Player player in new List<Player>(Saved.Keys))
            Remove(player);
    }
}
