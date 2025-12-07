using LoneEftDmaRadar.DMA.ScatterAPI;
using LoneEftDmaRadar.Tarkov.Features.MemWrites;
using LoneEftDmaRadar.Tarkov.GameWorld.Camera;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using System;
using System.Collections.Generic;
using System.Text;
using VmmSharpEx.Extensions;

namespace LoneEftDmaRadar.Tarkov.Features.Memwrites
{
    public sealed class NightVision : MemWriteFeature<NightVision>
    {
        private bool _currentState;
        private ulong _cachedThermalVisionComponent;

        // ? tell the base we must run once after disable
        protected override bool NeedsDisableCleanup => true;

        public override bool Enabled
        {
            get => App.Config.MemWrites.NightVisionEnabled;
            set => App.Config.MemWrites.NightVisionEnabled = value;
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

                bool targetState = Enabled;

                // If no change, do nothing
                if (targetState == _currentState)
                    return;

                var nightVisionComponent = GetNightVisionComponent();
                if (!nightVisionComponent.IsValidVA())
                    return;

                writes.AddValueEntry(nightVisionComponent + 0xC4, targetState);

                writes.Callbacks += () =>
                {
                    _currentState = targetState;
                    Debug.WriteLine($"[NightVision] {(targetState ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NightVision] ERROR: {ex}");
                _cachedThermalVisionComponent = 0;
            }
        }

        public ulong GetNightVisionComponent()
        {
            var fpsCamera = CameraManager.Current?.FPSCamera ?? 0;
            if (!fpsCamera.IsValidVA())
            {
                Debug.WriteLine("[NightVision] FPS Camera not found");
                return 0;
            }
            var nightVision = UnityComponent.GetComponentFromBehaviour(fpsCamera, "NightVision");
            if (!nightVision.IsValidVA())
            {
                Debug.WriteLine("[NightVision] NightVision component not found on FPS camera GO");
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
