// File: Tarkov/Features/MemWrites/MemWriteFeature.cs
using System;
using LoneEftDmaRadar.DMA.ScatterAPI;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    public abstract class MemWriteFeature<T> where T : MemWriteFeature<T>, new()
    {
        private static T _instance;
        private DateTime _lastRun = DateTime.MinValue;

        public static T Instance => _instance ??= new T();

        public abstract bool Enabled { get; set; }
        protected abstract TimeSpan Delay { get; }

        /// <summary>
        /// If true, this feature still wants to tick at least once
        /// after Enabled becomes false, so it can restore state
        /// (e.g. ThermalVision, ThirdPerson, WideLean).
        /// </summary>
        protected virtual bool NeedsDisableCleanup => false;

        public abstract void TryApply(LocalPlayer localPlayer);

        public virtual void TryApply(LocalPlayer localPlayer, ScatterWriteHandle writes)
        {
            TryApply(localPlayer);
        }

        public abstract void OnRaidStart();
        public abstract void OnRaidStopped();

        protected bool ShouldRun()
        {
            if (Delay <= TimeSpan.Zero)
            {
                _lastRun = DateTime.UtcNow;
                return true;
            }

            var now = DateTime.UtcNow;
            if (now - _lastRun < Delay)
                return false;

            _lastRun = now;
            return true;
        }

        /// <summary>
        /// Central gating used by MemWritesManager.
        /// </summary>
        public void ApplyIfReady(LocalPlayer localPlayer, ScatterWriteHandle writes)
        {
            if (!App.Config.MemWrites.Enabled)
                return;
            if (localPlayer is null)
                return;

            // If disabled *and* this feature doesn't need a cleanup pass, bail.
            if (!Enabled && !NeedsDisableCleanup)
                return;

            if (!ShouldRun())
                return;

            TryApply(localPlayer, writes);
        }
    }
}
