using LoneEftDmaRadar.DMA.ScatterAPI;
using LoneEftDmaRadar.Tarkov.GameWorld.Camera;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using VmmSharpEx.Extensions;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    public sealed class ThermalVision : MemWriteFeature<ThermalVision>
    {
        private bool _currentState;
        private ulong _cachedThermalVisionComponent;

        // ? tell the base we must run once after disable
        protected override bool NeedsDisableCleanup => true;

        public override bool Enabled
        {
            get => App.Config.MemWrites.ThermalEnabled;
            set => App.Config.MemWrites.ThermalEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(LocalPlayer localPlayer)
        {
            using var writes = ScatterWriteHandle.Create();
            TryApply(localPlayer, writes);
            writes.Execute();
        }

        public override void TryApply(LocalPlayer localPlayer, ScatterWriteHandle writes)
        {
            try
            {
                if (!Memory.InRaid || localPlayer is null)
                    return;

                // Old behavior: on only if enabled and not ADS
                bool targetState = Enabled && !localPlayer.CheckIfADS();

                // If no change, do nothing
                if (targetState == _currentState)
                    return;

                var thermalComponent = GetThermalVisionComponent();
                if (!thermalComponent.IsValidVA())
                    return;

                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.On, targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsNoisy, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsFpsStuck, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsMotionBlurred, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsGlitch, !targetState);
                writes.AddValueEntry(thermalComponent + Offsets.ThermalVision.IsPixelated, !targetState);
                writes.AddValueEntry(
                    thermalComponent + Offsets.ThermalVision.ChromaticAberrationThermalShift,
                    targetState ? 0f : 0.013f);
                writes.AddValueEntry(
                    thermalComponent + Offsets.ThermalVision.UnsharpRadiusBlur,
                    targetState ? 0.0001f : 5f);

                writes.Callbacks += () =>
                {
                    _currentState = targetState;
                    Debug.WriteLine($"[ThermalVision] {(targetState ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThermalVision] ERROR: {ex}");
                _cachedThermalVisionComponent = 0;
            }
        }

        private ulong GetThermalVisionComponent()
        {
            if (_cachedThermalVisionComponent.IsValidVA())
                return _cachedThermalVisionComponent;

            var fpsCamera = CameraManager.Current?.FPSCamera ?? 0;
            if (!fpsCamera.IsValidVA())
            {
                Debug.WriteLine("[ThermalVision] FPS Camera not found");
                return 0;
            }

            var thermal = UnityComponent.GetComponentFromBehaviour(fpsCamera, "ThermalVision");
            if (!thermal.IsValidVA())
            {
                Debug.WriteLine("[ThermalVision] ThermalVision component not found on FPS camera GO");
                return 0;
            }

            _cachedThermalVisionComponent = thermal;
            return thermal;
        }

        public ulong GetNightVisionComponent()
        {
            var fpsCamera = CameraManager.Current?.FPSCamera ?? 0;
            if (!fpsCamera.IsValidVA())
            {
                Debug.WriteLine("[ThermalVision] FPS Camera not found");
                return 0;
            }
            var nightVision = UnityComponent.GetComponentFromBehaviour(fpsCamera, "NightVision");
            if (!nightVision.IsValidVA())
            {
                Debug.WriteLine("[ThermalVision] NightVision component not found on FPS camera GO");
                return 0;
            }
            return nightVision;
        }

        public override void OnRaidStart()
        {
            _currentState = false;
            _cachedThermalVisionComponent = 0;
        }

        public override void OnRaidStopped()
        {
            _currentState = false;
            _cachedThermalVisionComponent = 0;
        }
    }
}
