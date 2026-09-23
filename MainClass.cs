using System;
using LabApi.Loader.Features.Plugins;
using Scp181.Events;
using Scp181.Services;

namespace Scp181
{
    public class MainClass : Plugin<Config>
    {
        public static MainClass? Instance { get; private set; }

        private Scp181Events? events;

        public override string Name => "Scp181";

        public override string Author => "ArcaneStrike";

        public override string Description => "SCP-181 Lucky Charm role with survival passives and an orange badge.";

        public override Version Version => new Version(2, 0, 2);

        public override Version RequiredApiVersion => new Version(1, 1, 7);

        /// <summary>Shared hint display layer; a null provider when HintServiceMeow is missing.</summary>
        internal IHintDisplayProvider Hints { get; private set; } = null!;

        public override void Enable()
        {
            if (!Config.IsEnabled || Instance == this)
                return;
            Instance = this;

            Hints = HintDisplayProviderFactory.Create(Config.HintDisplay);
            Hints.Enable();

            events = new Scp181Events();
            events.RegisterEvents();
        }

        public override void Disable()
        {
            events?.UnregisterEvents();
            events = null;

            Scp181Manager.Clear();

            Hints?.Disable();
            Hints = null!;

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }
    }
}
