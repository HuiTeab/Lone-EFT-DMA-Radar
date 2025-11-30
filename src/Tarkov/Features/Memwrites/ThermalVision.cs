using LoneEftDmaRadar.Tarkov.GameWorld.Camera;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Structures;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    ///
    /// </summary>
    public sealed class ThermalVision : MemWriteFeature<ThermalVision>
    {
        private bool _lastEnabledState;
        public override bool Enabled
        {
            get => App.Config.MemWrites.ThermalEnabled;
            set => App.Config.MemWrites.ThermalEnabled = value;
        }
        // Old version also ran at ~1s cadence
        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (localPlayer == null)
                    return;

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        _lastEnabledState = false;
                        Debug.WriteLine("[ThermalVision] Disabled");
                    }
                    return;
                }

                if (stateChanged)
                {
                    _lastEnabledState = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThermalVision] Exception: {ex}");
            }


        }

        private void ClearCache()
        {

        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            ClearCache();
        }
    }

}