
namespace Scp181.Services;

internal static class HintDisplayProviderFactory
{
    public static IHintDisplayProvider Create(HintDisplayConfig config)
    {
        if (HsmHintDisplayProvider.IsHsmLoaded)
        {
            HsmHintDisplayProvider hsmProvider = new(config);
            if (hsmProvider.TryInitialize(logResult: false))
            {
                return hsmProvider;
            }

            if (!config.EnableVanillaFallback)
            {
                return new NullHintDisplayProvider("HintServiceMeow.dll is loaded, but the required HSM API was not found.");
            }
        }

        if (config.EnableVanillaFallback)
        {
            return new VanillaCompatibilityHintProvider(config);
        }

        return new NullHintDisplayProvider("HintServiceMeow.dll is not loaded or was not loaded before this plugin.");
    }
}
