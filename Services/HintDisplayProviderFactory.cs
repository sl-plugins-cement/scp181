namespace Scp181.Services;

internal static class HintDisplayProviderFactory
{
    public static IHintDisplayProvider Create(HintDisplayConfig config) =>
        config.EnableVanillaFallback && !HsmAdapter.Hints.IsReady
            ? new VanillaCompatibilityHintProvider(config)
            : new HsmHintDisplayProvider(config);
}
