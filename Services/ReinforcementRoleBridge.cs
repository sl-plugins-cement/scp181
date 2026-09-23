using System;
using System.Linq;
using System.Reflection;
using LabApi.Features.Wrappers;
using Log = LabApi.Features.Console.Logger;
using LabPlayer = LabApi.Features.Wrappers.Player;

namespace Scp181.Services;

/// <summary>Optional queries against ReinforcementsSystem's own role ownership records.</summary>
internal static class ReinforcementRoleBridge
{
    private static Func<LabPlayer, bool>? _isTracked;
    private static Func<bool>? _isPending;
    private static bool _loggedFailure;

    private static bool Resolve()
    {
        if (_isTracked != null && _isPending != null) return true;
        Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "ReinforcementsSystem");
        if (assembly == null) return false;

        Type type = assembly.GetType("ReinforcementsSystem.ReinforcementsSystemPlugin", true)!;
        MethodInfo tracked = type.GetMethod("IsTrackedRole", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(LabPlayer) }, null)
            ?? throw new MissingMethodException(type.FullName, "IsTrackedRole");
        MethodInfo pending = type.GetProperty("IsInitialRoleSelectionPending", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod()
            ?? throw new MissingMethodException(type.FullName, "IsInitialRoleSelectionPending");
        _isTracked = (Func<LabPlayer, bool>)Delegate.CreateDelegate(typeof(Func<LabPlayer, bool>), tracked);
        _isPending = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), pending);
        return true;
    }

    public static bool IsSelectionPending
    {
        get
        {
            try { return Resolve() && _isPending!(); }
            catch (Exception ex) { LogFailure(ex); return true; }
        }
    }

    public static bool CanAssign(Player player)
    {
        try
        {
            if (!Resolve()) return true;
            return !_isPending!() && !_isTracked!(player);
        }
        catch (Exception ex) { LogFailure(ex); return false; }
    }

    private static void LogFailure(Exception ex)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        Log.Error($"[Scp181] Reinforcements role query failed; assignment is blocked. Install the matching ReinforcementsSystem build. {ex.Message}");
    }
}
