using LoneEftDmaRadar.Tarkov.GameWorld.Player;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    ///
    /// </summary>
    public sealed class Tester : MemWriteFeature<Tester>
    {
        private bool _lastEnabledState;

        public override bool Enabled
        {
            get => App.Config.MemWrites.TestEnabled;
            set => App.Config.MemWrites.TestEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(50);

        public override void TryApply(LocalPlayer localPlayer)
        {

            try
            {
                if (localPlayer == null)
                {
                    return;
                }

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    Debug.WriteLine("Tester disabled");
                    if (stateChanged)
                    {
                        _lastEnabledState = false;
                    }
                    return;
                }

                Debug.WriteLine("Tester enabled");



                if (stateChanged)
                {
                    _lastEnabledState = true;
                }
            }
            catch (Exception ex)
            {
                ClearCache();
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