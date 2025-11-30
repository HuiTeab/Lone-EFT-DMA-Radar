/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */


using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Manages all memory write features.
    /// </summary>
    public sealed class MemWritesManager
    {
        private readonly List<Action<LocalPlayer>> _features = new();
        private readonly Action<LocalPlayer> _thermalFeature;
        private bool _thermalRegistered;

        public MemWritesManager()
        {
            _thermalFeature = lp => ThermalVision.Instance.ApplyIfReady(lp);

            if (App.Config.MemWrites.ThermalEnabled)
            {
                _features.Add(_thermalFeature);
                _thermalRegistered = true;
            }
        }

        /// <summary>
        /// Apply all enabled memory write features.
        /// </summary>
        public void Apply(LocalPlayer localPlayer)
        {
            if (!App.Config.MemWrites.Enabled)
            {
                return;
            }

            if (localPlayer == null)
            {
                Debug.WriteLine("[MemWritesManager] LocalPlayer is null");
                return;
            }

            try
            {
                // Ensure runtime changes to settings register/unregister features.
                if (App.Config.MemWrites.ThermalEnabled && !_thermalRegistered)
                {
                    _features.Add(_thermalFeature);
                    _thermalRegistered = true;
                    //Debug.WriteLine("[MemWritesManager] Registered Thermal feature at runtime");
                }
                else if (!App.Config.MemWrites.ThermalEnabled && _thermalRegistered)
                {
                    // Explicitly invoke TryApply so the feature can observe Enabled==false
                    // and perform cleanup / log the disabled state. ApplyIfReady won't call
                    // TryApply when Enabled == false.
                    try
                    {
                        ThermalVision.Instance.TryApply(localPlayer);
                        //Debug.WriteLine("[MemWritesManager] Invoked ThermalVision.TryApply for shutdown");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MemWritesManager] Error invoking ThermalVision.TryApply during unregister: {ex}");
                    }

                    _features.Remove(_thermalFeature);
                    _thermalRegistered = false;
                   // Debug.WriteLine("[MemWritesManager] Unregistered Thermal feature at runtime");
                }

                if (!Memory.InRaid)
                    return;

                //Debug.WriteLine($"[MemWritesManager] Applying {_features.Count} features");

                foreach (var feature in _features)
                {
                    try
                    {
                        feature(localPlayer);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MemWritesManager] Feature error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MemWritesManager] Apply error: {ex}");
            }
        }

        /// <summary>
        /// Called when raid starts.
        /// </summary>
        public void OnRaidStart()
        {
            if (App.Config.MemWrites.ThermalEnabled)
                ThermalVision.Instance.OnRaidStart();
        }
    }
}