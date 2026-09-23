using System.Collections.Generic;
using System.Reflection;
using PlayerRoles.PlayableScps.Scp3114;
using PlayerStatsSystem;

namespace Scp181;

/// <summary>Native damage classification without a framework-specific DamageType enum.</summary>
internal static class DamageSources
{
    private static readonly Dictionary<byte, string> TranslationNames = BuildTranslationNames();

    public static bool IsScpAttack(StandardDamageHandler damage)
        => damage is ScpDamageHandler || damage is Scp3114DamageHandler;

    public static bool IsStatusEffect(StandardDamageHandler damage)
    {
        if (damage is not UniversalDamageHandler universal)
            return false;
        byte id = universal.TranslationId;
        return id == DeathTranslations.Asphyxiated.Id || id == DeathTranslations.Bleeding.Id
            || id == DeathTranslations.Poisoned.Id || id == DeathTranslations.Scp207.Id
            || id == DeathTranslations.SeveredHands.Id || id == DeathTranslations.Hypothermia.Id;
    }

    public static string GetName(StandardDamageHandler damage)
    {
        if (damage is FirearmDamageHandler firearm)
            return firearm.WeaponType.ToString();
        if (damage is Scp3114DamageHandler skeleton)
            return skeleton.Subtype == Scp3114DamageHandler.HandlerType.Strangulation ? "Strangled" : "Scp3114";
        if (damage is Scp049DamageHandler doctor)
            return doctor.DamageSubType == Scp049DamageHandler.AttackType.Scp0492 ? "Scp0492" : "Scp049";
        if (damage is Scp096DamageHandler)
            return "Scp096";
        if (damage is ScpDamageHandler scp)
            return scp.Attacker.Role.ToString();
        if (damage is UniversalDamageHandler universal)
            return TranslationNames.TryGetValue(universal.TranslationId, out string name) ? name : "Unknown";
        string type = damage.GetType().Name;
        const string suffix = "DamageHandler";
        return type.EndsWith(suffix, System.StringComparison.Ordinal)
            ? type.Substring(0, type.Length - suffix.Length) : type;
    }

    private static Dictionary<byte, string> BuildTranslationNames()
    {
        var names = new Dictionary<byte, string>();
        foreach (FieldInfo field in typeof(DeathTranslations).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(DeathTranslation))
                names[((DeathTranslation)field.GetValue(null)).Id] = field.Name;

        names[DeathTranslations.PocketDecay.Id] = "PocketDimension";
        names[DeathTranslations.Falldown.Id] = "Fall";
        names[DeathTranslations.Poisoned.Id] = "Poison";
        names[DeathTranslations.Asphyxiated.Id] = "Asphyxiation";
        names[DeathTranslations.Zombie.Id] = "Scp0492";
        return names;
    }
}
