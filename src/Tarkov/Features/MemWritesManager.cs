/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.DMA.ScatterAPI;
using LoneEftDmaRadar.Tarkov.Features.Memwrites;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Manages all memory write features (new project).
    ///
    /// One ScatterWriteHandle (VmmScatter) per tick, all features add writes,
    /// then Execute() once at the end.
    /// </summary>
    public sealed class MemWritesManager
    {
        private readonly List<Action<LocalPlayer, ScatterWriteHandle>> _features = new();

        public MemWritesManager()
        {
            _features.Add((lp, w) => ThermalVision.Instance.ApplyIfReady(lp, w));
            _features.Add((lp, w) => NightVision.Instance.ApplyIfReady(lp, w));
        }

        /// <summary>
        /// Apply all enabled memory write features.
        /// Call this from your main loop with current LocalPlayer.
        /// </summary>
        public void Apply(LocalPlayer localPlayer)
        {
            if (!App.Config.MemWrites.Enabled)
                return;

            if (!Memory.InRaid)
                return;

            if (localPlayer == null)
            {
                Debug.WriteLine("[MemWritesManager] LocalPlayer is null");
                return;
            }

            using var writes = ScatterWriteHandle.Create();

            try
            {
                foreach (var feature in _features)
                {
                    try
                    {
                        feature(localPlayer, writes);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MemWritesManager] Feature error: {ex}");
                    }
                }

                writes.Execute();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MemWritesManager] Apply error: {ex}");
            }
        }

        public void OnRaidStart()
        {
            ThermalVision.Instance.OnRaidStart();
            NightVision.Instance.OnRaidStart();
        }

        public void OnRaidStopped()
        {
            ThermalVision.Instance.OnRaidStopped();
            NightVision.Instance.OnRaidStopped();
        }
    }
}
