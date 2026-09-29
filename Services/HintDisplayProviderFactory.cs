using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Scp181.Services;

internal static class HintDisplayProviderFactory
{
    public static IHintDisplayProvider Create(HintDisplayConfig config)
    {
        try
        {
            return CreateWithAdapter(config);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            // The role and its passives must still work when the UI dependency is missing.
            return config.EnableVanillaFallback
                ? new VanillaCompatibilityHintProvider(config)
                : new NullHintDisplayProvider("HsmAdapter.dll is missing or incompatible.");
        }
    }

    // Kept separate so a missing HsmAdapter assembly fails here, inside Create's handler.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IHintDisplayProvider CreateWithAdapter(HintDisplayConfig config) =>
        config.EnableVanillaFallback && !HsmAdapter.Hints.IsReady
            ? new VanillaCompatibilityHintProvider(config)
            : new HsmHintDisplayProvider(config);
}
