using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using PlayerRoles.PlayableScps.Scp3114;
using PlayerStatsSystem;
using Scp181;

// Uses the actual target-server types, without starting Unity or invoking gameplay constructors.
internal static class RegressionChecks
{
    private static int checks;

    public static int Main(string[] args)
    {
        try
        {
            var pocket = Universal(DeathTranslations.PocketDecay);
            Check(DamageSources.GetName(pocket) == "PocketDimension", "pocket exit classification");
            Check(!DamageSources.IsStatusEffect(pocket), "pocket decay must not be immune");
            Check(!DamageSources.IsScpAttack(pocket), "pocket trap sentinel must remain lethal after escape charge");
            foreach (var translation in new[] { DeathTranslations.Bleeding, DeathTranslations.Poisoned,
                DeathTranslations.Hypothermia, DeathTranslations.Scp207, DeathTranslations.SeveredHands })
                Check(DamageSources.IsStatusEffect(Universal(translation)), "debuff immunity: " + translation.Id);

            Check(!DamageSources.IsStatusEffect(Universal(DeathTranslations.CardiacArrest)), "preserve cardiac damage");
            Check(!DamageSources.IsStatusEffect(Universal(DeathTranslations.Decontamination)), "preserve decontamination");
            Check(DamageSources.GetName(Universal(DeathTranslations.Falldown)) == "Fall", "fall alias");

            var skeleton = Uninitialized<Scp3114DamageHandler>();
            SetProperty(skeleton, "Subtype", Scp3114DamageHandler.HandlerType.Strangulation);
            Check(DamageSources.GetName(skeleton) == "Strangled", "strangulation bypasses dodge");
            Check(DamageSources.IsScpAttack(skeleton), "strangulation uses SCP cap");
            Check(!DamageSources.IsStatusEffect(skeleton), "strangulation must not be immune");

            var doctor = Uninitialized<Scp049DamageHandler>();
            Check(DamageSources.GetName(doctor) == "Scp049", "doctor instant kill classification");
            Check(DamageSources.IsScpAttack(doctor), "doctor uses SCP cap without connected attacker");
            SetProperty(doctor, "DamageSubType", Scp049DamageHandler.AttackType.Scp0492);
            Check(DamageSources.GetName(doctor) == "Scp0492", "zombie classification");
            Check(!DamageSources.IsScpAttack(Uninitialized<Scp018DamageHandler>()), "SCP item is not SCP role damage");
            Check(!DamageSources.IsScpAttack(Uninitialized<WarheadDamageHandler>()), "warhead bypasses SCP cap");

            var firearm = Uninitialized<FirearmDamageHandler>();
            SetProperty(firearm, "WeaponType", ItemType.GunE11SR);
            Check(DamageSources.GetName(firearm) == "GunE11SR", "native weapon override key");
            if (args.Length != 1)
                throw new ArgumentException("Pass the built Scp181.dll path to verify framework dependencies.");
            var references = Assembly.ReflectionOnlyLoadFrom(args[0]).GetReferencedAssemblies();
            Check(references.Any(a => a.Name == "LabApi"), "LabAPI reference exists");
            Check(!references.Any(a => a.Name.StartsWith("Exiled", StringComparison.OrdinalIgnoreCase)), "no EXILED references");
            Console.WriteLine($"PASS: {checks} regression checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void SetProperty(object obj, string name, object value)
        => obj.GetType().GetProperty(name).SetValue(obj, value);
    private static UniversalDamageHandler Universal(DeathTranslation translation)
    {
        var handler = Uninitialized<UniversalDamageHandler>();
        typeof(UniversalDamageHandler).GetField("TranslationId").SetValue(handler, translation.Id);
        return handler;
    }
    private static void Check(bool passed, string name)
    {
        if (!passed) throw new Exception("FAIL: " + name);
        checks++;
    }
}
