using Exiled.API.Features;
using Scp181.Events;

namespace Scp181
{
    public class MainClass : Plugin<Config>
    {
        public static MainClass Instance { get; private set; }
        private Scp181Events events;

        public override string Name => "Scp181";
        public override string Author => "ArcaneStrike";
        public override string Prefix => "scp181";
        public override System.Version Version => new System.Version(1, 0, 0);

        public override void OnEnabled()
        {
            Instance = this;
            events = new Scp181Events();
            events.RegisterEvents();
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            events?.UnregisterEvents();
            events = null;
            if (ReferenceEquals(Instance, this))
                Instance = null;
            base.OnDisabled();
        }
    }
}