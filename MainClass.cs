using System;
using Exiled.API.Features;
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

        public override string Prefix => "scp181";

        public override Version Version => new Version(1, 1, 0);

        public override Version RequiredExiledVersion => new Version(9, 14, 0);

        /// <summary>Shared hint display layer; a null provider when HintServiceMeow is missing.</summary>
        internal IHintDisplayProvider Hints { get; private set; } = null!;

        public override void OnEnabled()
        {
            Instance = this;

            Hints = HintDisplayProviderFactory.Create(Config.HintDisplay);
            Hints.Enable();

            events = new Scp181Events();
            events.RegisterEvents();

            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            events?.UnregisterEvents();
            events = null;

            Scp181Manager.Clear();

            Hints?.Disable();
            Hints = null!;

            if (ReferenceEquals(Instance, this))
                Instance = null;

            base.OnDisabled();
        }
    }
}
