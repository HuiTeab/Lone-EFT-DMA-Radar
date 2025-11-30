/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using SkiaSharp;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using VmmSharpEx;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Camera
{
    public sealed class CameraManager
    {
        private static CameraManager _current;
        public static CameraManager Current => _current;

        private Thread _initThread;
        private bool _initThreadRunning;

        public ulong FPSCamera { get; private set; }
        public ulong OpticCamera { get; private set; }

        // Static debug copies
        public static ulong FPSCameraPtr { get; private set; }
        public static ulong OpticCameraPtr { get; private set; }
        public static ulong ActiveCameraPtr { get; private set; }


        public CameraManager()
        {
            _current = this;

            Debug.WriteLine("=== CameraManager Initialization ===");
            Debug.WriteLine($"Unity Base: 0x{Memory.UnityBase:X}");
            Debug.WriteLine($"AllCameras Offset: 0x{UnitySDK.UnityOffsets.AllCameras:X}");

            StartInitThread();

        }
        private void StartInitThread()
        {
            // If a thread is already alive, don't spawn another
            if (_initThread != null && _initThread.IsAlive)
                return;

            _initThreadRunning = true;
            _initThread = new Thread(InitializationLoop)
            {
                IsBackground = true,
                Name = "CameraManager Initialization"
            };
            _initThread.Start();
        }

        private void InitializationLoop()
        {
            int attemptNumber = 0;
            DateTime lastLogTime = DateTime.MinValue;

            // Only do the “old raid cameras” wait when we’re starting from a fresh state
            Debug.WriteLine("[CameraManager] Waiting 15 seconds for old raid cameras to be cleaned up...");
            Thread.Sleep(15000);
            Debug.WriteLine("[CameraManager] Starting camera search for raid...");

            while (_initThreadRunning)
            {
                try
                {
                    attemptNumber++;
                    bool shouldLog = (DateTime.UtcNow - lastLogTime).TotalSeconds >= 5.0;

                    if (shouldLog)
                    {
                        Debug.WriteLine($"[CameraManager] Initialization attempt #{attemptNumber}...");
                        lastLogTime = DateTime.UtcNow;
                    }

                    if (TryInitializeCameras(shouldLog))
                    {
                        Debug.WriteLine($"[CameraManager] ✓✓✓ Successfully initialized after {attemptNumber} attempts! ✓✓✓");

                        // Let the loop exit; thread will die.
                        // On next raid, we will call StartInitThread() again.
                        _initThreadRunning = false;
                        return;
                    }

                    Thread.Sleep(attemptNumber < 10 ? 500 : 1000);
                }
                catch (Exception ex)
                {
                    if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 5.0)
                    {
                        Debug.WriteLine($"[CameraManager] Initialization error (will retry): {ex.Message}");
                        lastLogTime = DateTime.UtcNow;
                    }
                    Thread.Sleep(1000);
                }
            }
        }

        private bool TryInitializeCameras(bool verbose)
        {
            try
            {
                // ✅ NEW: don't even try to lock in cameras until the raid / LocalGameWorld is ready
                if (!Memory.InRaid || Memory.LocalPlayer == null)
                {
                    if (verbose)
                    {
                        Debug.WriteLine("[CameraManager] InRaid/LocalPlayer not ready yet - waiting for LocalGameWorld before camera init...");
                    }
                    return false;
                }
                Thread.Sleep(10000); // Give game a moment to spawn cameras

                // Calculate AllCameras address
                var allCamerasAddr = Memory.UnityBase + UnitySDK.UnityOffsets.AllCameras;
                var allCamerasPtr = Memory.ReadPtr(allCamerasAddr, false);

                if (allCamerasPtr == 0 || allCamerasPtr > 0x7FFFFFFFFFFF)
                {
                    if (verbose)
                        Debug.WriteLine($"[CameraManager] AllCameras pointer invalid: 0x{allCamerasPtr:X} (waiting for raid load...)");
                    return false;
                }

                // AllCameras is a List<Camera*>
                var listItemsPtr = Memory.ReadPtr(allCamerasPtr + 0x0, false);
                var count = Memory.ReadValue<int>(allCamerasPtr + 0x8, false);

                if (listItemsPtr == 0 || count <= 0)
                {
                    if (verbose)
                        Debug.WriteLine($"[CameraManager] No cameras in list yet (count: {count}) - waiting for raid to load...");
                    return false;
                }

                if (verbose)
                {
                    Debug.WriteLine($"[CameraManager] Found {count} cameras in list, searching...");
                }

                // Find cameras by name
                var (fps, optic) = FindCamerasByName(listItemsPtr, count, verbose);

                if (fps == 0)
                {
                    if (verbose)
                        Debug.WriteLine($"[CameraManager] FPS Camera not found yet (waiting for raid to spawn cameras...)");
                    return false;
                }

                if (optic == 0)
                {
                    if (verbose)
                        Debug.WriteLine($"[CameraManager] Optic Camera not found yet (waiting...)");
                    return false;
                }

            }
            catch (Exception ex)
            {
                if (verbose)
                    Debug.WriteLine($"[CameraManager] Exception during camera initialization: {ex.Message}");

            }
            return true;
        }

        private static (ulong fpsCamera, ulong opticCamera) FindCamerasByName(ulong listItemsPtr, int count, bool verbose)
        {
            ulong fpsCamera = 0;
            ulong opticCamera = 0;

            if (verbose)
            {
                Debug.WriteLine($"[CameraManager] Scanning {count} cameras for FPS/Optic...");
            }

            for (int i = 0; i < Math.Min(count, 100); i++)
            {
                try
                {
                    // Each item in the array is a pointer (8 bytes)
                    ulong cameraEntryAddr = listItemsPtr + (uint)(i * 0x8);
                    var cameraPtr = Memory.ReadPtr(cameraEntryAddr, false);

                    if (cameraPtr == 0 || cameraPtr > 0x7FFFFFFFFFFF)
                        continue;

                    // Camera+0x50 -> GameObject
                    var gameObjectPtr = Memory.ReadPtr(cameraPtr + UnitySDK.UnityOffsets.Component_GameObjectOffset, false);
                    if (gameObjectPtr == 0 || gameObjectPtr > 0x7FFFFFFFFFFF)
                        continue;

                    // GameObject+0x78 -> Name string pointer
                    var namePtr = Memory.ReadPtr(gameObjectPtr + UnitySDK.UnityOffsets.GameObject_NameOffset, false);
                    if (namePtr == 0 || namePtr > 0x7FFFFFFFFFFF)
                        continue;

                    // Read the name string
                    var name = Memory.ReadUtf8String(namePtr, 64, false);
                    if (string.IsNullOrEmpty(name) || name.Length < 3)
                        continue;

                    if (verbose)
                    {
                        Debug.WriteLine($"  [{i:D2}] Camera: '{name}' @ 0x{cameraPtr:X}");
                    }

                    // Check for FPS Camera
                    bool isFPS = name.Contains("FPS", StringComparison.OrdinalIgnoreCase) &&
                                name.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                    // Check for Optic Camera  
                    bool isOptic = (name.Contains("Optic", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("BaseOptic", StringComparison.OrdinalIgnoreCase)) &&
                                  name.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                    if (isFPS)
                    {
                        fpsCamera = cameraPtr;
                        if (verbose)
                            Debug.WriteLine($"       ✓✓✓ MATCHED AS FPS CAMERA ✓✓✓");
                    }

                    if (isOptic)
                    {
                        opticCamera = cameraPtr;
                        if (verbose)
                            Debug.WriteLine($"       ✓✓✓ MATCHED AS OPTIC CAMERA ✓✓✓");
                    }

                    if (fpsCamera != 0 && opticCamera != 0)
                    {
                        if (verbose)
                            Debug.WriteLine($"[CameraManager] Both cameras found! Stopping search at index {i}.");
                        break;
                    }
                }
                catch
                {
                    // Silently skip bad entries during retry loop
                }
            }

            if (verbose)
            {
                Debug.WriteLine($"[CameraManager] Search Results:");
                Debug.WriteLine($"  FPS Camera:   {(fpsCamera != 0 ? $"✓ Found @ 0x{fpsCamera:X}" : "✗ NOT FOUND")}");
                Debug.WriteLine($"  Optic Camera: {(opticCamera != 0 ? $"✓ Found @ 0x{opticCamera:X}" : "✗ NOT FOUND")}");
            }

            FPSCameraPtr = fpsCamera;
            OpticCameraPtr = opticCamera;

            return (fpsCamera, opticCamera);
        }
    }
}
