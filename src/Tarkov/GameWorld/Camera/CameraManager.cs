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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VmmSharpEx;
using VmmSharpEx.Scatter;
using System.Collections.Generic;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using System;
using System.Threading;
using LoneEftDmaRadar.Tarkov.Unity.Collections;

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

        // Validation tracking
        private int _consecutiveMatrixFailures;
        private DateTime _lastValidMatrix = DateTime.MinValue;
        private bool _matrixInitialized;

        // Raid timing
        private DateTime _raidStartTime = DateTime.MinValue;

        private static readonly TimeSpan RaidStartDelay = TimeSpan.FromSeconds(3);

        public CameraManager()
        {
            if (_current != null)
            {
                Debug.WriteLine("[CameraManager] WARNING: Duplicate CameraManager constructed; using existing instance.");
                return;
            }

            _current = this;
            Memory.CameraManager = this;

            Debug.WriteLine("=== CameraManager Initialization ===");
            Debug.WriteLine($"Unity Base: 0x{Memory.UnityBase:X}");
            Debug.WriteLine($"AllCameras Offset: 0x{UnitySDK.UnityOffsets.AllCameras:X}");

            StartInitThread();
        }

        /// <summary>
        /// Cleanly stops the initialization / monitoring thread.
        /// Call this when a raid/game loop ends and this instance is being discarded.
        /// </summary>
        public void Shutdown()
        {
            _initThreadRunning = false;
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

        /// <summary>
        /// Background thread – state machine:
        /// - Waits for raid
        /// - Waits 30s after raid start
        /// - Tries to initialize cameras
        /// - When initialized, just sits idle (no more auto re-inits)
        /// - Clears state when raid ends
        /// </summary>
        private void InitializationLoop()
        {
            int attemptNumber = 0;
            DateTime lastLogTime = DateTime.MinValue;

            Debug.WriteLine("[CameraManager] Init thread started - waiting for raid...");

            while (_initThreadRunning)
            {
                try
                {
                    // ─────────────────────────────
                    // 1) Wait until we're actually in raid
                    // ─────────────────────────────
                    if (!Memory.InRaid || Memory.LocalPlayer == null)
                    {
                        if (_matrixInitialized || FPSCamera != 0 || OpticCamera != 0)
                        {
                            Debug.WriteLine("[CameraManager] Detected raid exit in init loop - clearing camera state");
                            ClearCameraStateInternal();
                        }

                        _raidStartTime = DateTime.MinValue;
                        Thread.Sleep(500);
                        continue;
                    }

                    // We are in-raid here
                    if (_raidStartTime == DateTime.MinValue)
                    {
                        _raidStartTime = DateTime.UtcNow;
                        Debug.WriteLine("[CameraManager] Raid detected - waiting 30 seconds before camera search...");
                        attemptNumber = 0;
                    }

                    // ─────────────────────────────
                    // 2) Enforce 30s delay after raid start before looking at cameras
                    // ─────────────────────────────
                    var sinceRaidStart = DateTime.UtcNow - _raidStartTime;
                    if (sinceRaidStart < RaidStartDelay)
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    // ─────────────────────────────
                    // 3) If already initialized, just chill
                    //    (matrix will keep updating via OnRealtimeLoop)
                    // ─────────────────────────────
                    if (_matrixInitialized)
                    {
                        Thread.Sleep(500);
                        continue;
                    }

                    // ─────────────────────────────
                    // 4) Not initialized yet – try to initialize cameras
                    // ─────────────────────────────
                    attemptNumber++;
                    bool shouldLog = (DateTime.UtcNow - lastLogTime).TotalSeconds >= 5.0;

                    if (shouldLog)
                    {
                        Debug.WriteLine($"[CameraManager] Initialization attempt #{attemptNumber} (post-raid delay)...");
                        lastLogTime = DateTime.UtcNow;
                    }

                    if (TryInitializeCameras(shouldLog))
                    {
                        Debug.WriteLine($"[CameraManager] ✓✓✓ Successfully initialized after {attemptNumber} attempts! ✓✓✓");
                        // Do NOT exit loop – we may want to watch raid exit
                    }

                    Thread.Sleep(attemptNumber < 10 ? 500 : 1000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CameraManager] InitializationLoop error (will continue): {ex}");
                    Thread.Sleep(1000);
                }
            }

            Debug.WriteLine("[CameraManager] Init thread exiting (Shutdown called).");
        }

        /// <summary>
        /// Try to initialize cameras. Returns true if successful.
        /// </summary>
        private bool TryInitializeCameras(bool verbose)
        {
            try
            {
                if (!Memory.InRaid || Memory.LocalPlayer == null)
                {
                    if (verbose)
                        Debug.WriteLine("[CameraManager] InRaid/LocalPlayer not ready yet...");
                    return false;
                }

                Thread.Sleep(1000);

                var allCamerasAddr = Memory.UnityBase + UnitySDK.UnityOffsets.AllCameras;
                Debug.WriteLine($"[CameraManager] AllCamerasAddr = 0x{allCamerasAddr:X}");

                var allCamerasPtr = Memory.ReadPtr(allCamerasAddr, false);
                Debug.WriteLine($"[CameraManager] allCamerasPtr   = 0x{allCamerasPtr:X}");

                if (allCamerasPtr == 0 || allCamerasPtr > 0x7FFF_FFFF_FFFFul)
                {
                    if (verbose)
                        Debug.WriteLine($"[CameraManager] AllCameras pointer invalid: 0x{allCamerasPtr:X}");
                    return false;
                }

                ulong listItemsPtr;
                int count;

                try
                {
                    listItemsPtr = Memory.ReadPtr(allCamerasPtr + 0x0, false);
                    count = Memory.ReadValue<int>(allCamerasPtr + 0x8, false);
                    Debug.WriteLine($"[CameraManager] listItemsPtr   = 0x{listItemsPtr:X}, count={count}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CameraManager] ERROR reading AllCameras list: {ex}");
                    return false;
                }

                if (listItemsPtr == 0 || count <= 0 || count > 1024)
                {
                    if (verbose)
                        Debug.WriteLine($"[CameraManager] No cameras in list yet (count: {count})");
                    return false;
                }

                if (verbose)
                    Debug.WriteLine($"[CameraManager] Found {count} cameras in list, searching...");

                var (fps, optic) = FindCamerasByNameSafe(listItemsPtr, count, verbose);



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

                FPSCamera = fps;
                OpticCamera = optic;
                FPSCameraPtr = fps;
                OpticCameraPtr = optic;
                ActiveCameraPtr = 0;

                if (verbose)
                {
                    Debug.WriteLine($"[CameraManager] ✓ FPS Camera: 0x{FPSCamera:X}");
                    Debug.WriteLine($"[CameraManager] ✓ Optic Camera: 0x{OpticCamera:X}");
                }

                // Validate FPS camera matrix using CameraPtr + ViewMatrixOffset
                bool fpsValid = VerifyViewMatrix(FPSCamera, verbose ? "FPS" : null);

                if (fpsValid)
                {
                    _matrixInitialized = true;
                    _lastValidMatrix = DateTime.UtcNow;

                    Debug.WriteLine("[CameraManager] ✓ FPS Camera validated successfully - READY!");

                    return true;
                }
                else
                {
                    if (verbose)
                        Debug.WriteLine("[CameraManager] FPS camera matrix not yet valid (waiting for game to fully load...)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    Debug.WriteLine($"[CameraManager] TryInitializeCameras error: {ex.Message}");
                return false;
            }
        }
        private static (ulong fpsCamera, ulong opticCamera) FindCamerasByNameSafe(
            ulong listItemsPtr, int count, bool verbose)
        {
            ulong fpsCamera = 0;
            ulong opticCamera = 0;

            for (int i = 0; i < Math.Min(count, 100); i++)
            {
                try
                {
                    ulong cameraEntryAddr = listItemsPtr + (uint)(i * 0x8);
                    var cameraPtr = Memory.ReadPtr(cameraEntryAddr, false);

                    if (cameraPtr == 0 || cameraPtr > 0x7FFF_FFFF_FFFFul)
                        continue;

                    var gameObjectPtr =
                        Memory.ReadPtr(cameraPtr + UnitySDK.UnityOffsets.Component_GameObjectOffset, false);
                    if (gameObjectPtr == 0 || gameObjectPtr > 0x7FFF_FFFF_FFFFul)
                        continue;

                    var namePtr =
                        Memory.ReadPtr(gameObjectPtr + UnitySDK.UnityOffsets.GameObject_NameOffset, false);
                    if (namePtr == 0 || namePtr > 0x7FFF_FFFF_FFFFul)
                        continue;

                    var name = Memory.ReadUtf8String(namePtr, 64, false);
                    if (string.IsNullOrEmpty(name) || name.Length < 3)
                        continue;

                    if (verbose)
                        Debug.WriteLine($"  [{i:D2}] Camera: '{name}' @ 0x{cameraPtr:X}");

                    bool isFPS = name.Contains("FPS", StringComparison.OrdinalIgnoreCase) &&
                                 name.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                    bool isOptic = (name.Contains("Optic", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("BaseOptic", StringComparison.OrdinalIgnoreCase)) &&
                                   name.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                    if (isFPS)
                        fpsCamera = cameraPtr;

                    if (isOptic)
                        opticCamera = cameraPtr;

                    if (fpsCamera != 0 && opticCamera != 0)
                        break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CameraManager] FindCamerasByNameSafe: index {i} error: {ex.Message}");
                }
            }

            return (fpsCamera, opticCamera);
        }

        /// <summary>
        /// Verifies a camera's view matrix has valid data.
        /// Expects cameraPtr (not a pre-resolved matrix address).
        /// ALSO seeds _viewMatrix when valid.
        /// </summary>
        private static bool VerifyViewMatrix(ulong cameraPtr, string name)
        {
            var matrixAddr = cameraPtr + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset;
            var vm = Memory.ReadValue<Matrix4x4>(matrixAddr, false);

            if (!string.IsNullOrEmpty(name))
            {
                Debug.WriteLine($"\n{name} Matrix @ 0x{matrixAddr:X} (Camera: 0x{cameraPtr:X}):");
                Debug.WriteLine($"  M11: {vm.M11:F6}, M22: {vm.M22:F6}, M33: {vm.M33:F6}, M44: {vm.M44:F6}");
                Debug.WriteLine($"  Translation: ({vm.M41:F2}, {vm.M42:F2}, {vm.M43:F2})");
            }

            // Check for NaN/Infinity
            if (float.IsNaN(vm.M11) || float.IsInfinity(vm.M11) ||
                float.IsNaN(vm.M22) || float.IsInfinity(vm.M22) ||
                float.IsNaN(vm.M33) || float.IsInfinity(vm.M33) ||
                float.IsNaN(vm.M44) || float.IsInfinity(vm.M44) ||
                float.IsNaN(vm.M41) || float.IsInfinity(vm.M41))
            {
                if (!string.IsNullOrEmpty(name))
                    Debug.WriteLine($"  ✗ Invalid: Contains NaN/Infinity");
                return false;
            }

            // Check for all-zeros
            if (vm.M11 == 0f && vm.M22 == 0f && vm.M33 == 0f && vm.M44 == 0f)
            {
                if (!string.IsNullOrEmpty(name))
                    Debug.WriteLine($"  ✗ Invalid: All zeros");
                return false;
            }

            // Check translation is within reasonable bounds
            if (Math.Abs(vm.M41) > 5000f || Math.Abs(vm.M42) > 5000f || Math.Abs(vm.M43) > 5000f)
            {
                if (!string.IsNullOrEmpty(name))
                    Debug.WriteLine($"  ✗ Invalid: Translation out of bounds");
                return false;
            }

            // Reject origin-ish translations when we *are* in raid
            if (Memory.InRaid)
            {
                float tx = vm.M41;
                float ty = vm.M42;
                float tz = vm.M43;

                float lenSq = tx * tx + ty * ty + tz * tz;
                if (lenSq < 1.0f) // ~distance < 1m from origin
                {
                    if (!string.IsNullOrEmpty(name))
                        Debug.WriteLine($"  ✗ Invalid: Translation too close to origin while in raid");
                    return false;
                }
            }

            // If we got here, matrix is valid -> seed static view matrix
            _viewMatrix.Update(ref vm);

            if (_current is { } inst)
            {
                inst._lastValidMatrix = DateTime.UtcNow;
            }

            if (!string.IsNullOrEmpty(name))
                Debug.WriteLine($"  ✓ Valid: Matrix looks good");

            return true;
        }

        static CameraManager()
        {
            // Ensure viewport is valid even if ESP window never gets created.
            try
            {
                UpdateViewportRes();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraManager] Failed to init viewport in static ctor: {ex}");
            }

            Memory.ProcessStarting += MemDMA_ProcessStarting;
            Memory.ProcessStopped += MemDMA_ProcessStopped;
            Memory.RaidStopped += MemDMA_RaidStopped;
        }

        private static void MemDMA_RaidStopped(object sender, EventArgs e)
        {
            Debug.WriteLine("[CameraManager] Raid stopped - clearing camera state");

            if (_current is { } inst)
            {
                inst.ClearCameraStateInternal();
            }
        }

        private static void MemDMA_ProcessStarting(object sender, EventArgs e) { }
        private static void MemDMA_ProcessStopped(object sender, EventArgs e) { }

        /// <summary>
        /// Clears all camera-related state and view matrix. Does NOT touch _raidStartTime;
        /// that is reset by the init loop when it sees we're no longer in raid.
        /// </summary>
        private void ClearCameraStateInternal()
        {
            var identity = Matrix4x4.Identity;
            _viewMatrix.Update(ref identity);
            EspRunning = false;

            FPSCameraPtr = 0;
            OpticCameraPtr = 0;
            ActiveCameraPtr = 0;
            _zoomLevel = 1.0f;
            _fov = 0f;
            _aspect = 0f;

            _matrixInitialized = false;
            _consecutiveMatrixFailures = 0;
            _lastValidMatrix = DateTime.MinValue;
            FPSCamera = 0;
            OpticCamera = 0;
        }

        /// <summary>
        /// Uses sight component to determine if we are actually scoped.
        /// Mirrors old project behavior: requires optic camera active + zoom > 1.
        /// </summary>
        private bool CheckIfScoped(LocalPlayer localPlayer)
        {
            try
            {
                if (localPlayer is null)
                {
                    _zoomLevel = 1.0f;
                    return false;
                }

                // Old behavior: only consider scoped if optic camera is active
                if (!OpticCameraActive)
                {
                    _zoomLevel = 1.0f;
                    return false;
                }

                var opticsPtr = Memory.ReadPtr(
                    localPlayer.PWA + Offsets.ProceduralWeaponAnimation._optics);

                if (opticsPtr == 0 || opticsPtr > 0x7FFF_FFFF_FFFFul)
                {
                    _zoomLevel = 1.0f;
                    return false;
                }

                using var optics = UnityList<VmmPointer>.Create(opticsPtr, true);
                if (optics.Count <= 0)
                {
                    _zoomLevel = 1.0f;
                    return false;
                }

                var pSightComponent = Memory.ReadPtr(
                    optics[0] + Offsets.SightNBone.Mod);

                var sightComponent = Memory.ReadValue<SightComponent>(pSightComponent);

                float zoom;

                // Prefer ScopeZoomValue if non-zero
                if (sightComponent.ScopeZoomValue != 0f)
                {
                    zoom = sightComponent.ScopeZoomValue;
                }
                else
                {
                    zoom = sightComponent.GetZoomLevel();
                }

                if (!zoom.IsNormalOrZero() || zoom <= 0f || zoom >= 100f)
                {
                    _zoomLevel = 1.0f;
                    return false;
                }

                _zoomLevel = zoom;

                // Scoped only if zoom > 1
                return zoom > 1.0f;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckIfScoped() ERROR: {ex}");
                _zoomLevel = 1.0f;
                return false;
            }
        }

        private bool OpticCameraActive
        {
            get
            {
                try
                {
                    if (OpticCamera == 0)
                        return false;

                    return Memory.ReadValue<bool>(
                        OpticCamera + UnitySDK.UnityOffsets.MonoBehaviour_IsAddedOffset,
                        false);
                }
                catch
                {
                    return false;
                }
            }
        }
        public void OnRealtimeLoop(VmmScatter scatter, LocalPlayer localPlayer)
        {
            try
            {
                // Basic guards
                if (!Memory.InRaid || localPlayer == null)
                {
                    _zoomLevel = 1.0f;
                    IsScoped = false;
                    IsADS = false;
                    return;
                }

                // ADS / scope state
                IsADS = localPlayer.CheckIfADS();
                if (!IsADS)
                {
                    _zoomLevel = 1.0f;
                    IsScoped = false;
                }
                else
                {
                    IsScoped = CheckIfScoped(localPlayer);
                }

                // When scoped, use OPTIC camera for view matrix,
                // otherwise FPS camera
                ulong viewCamera = (IsScoped && OpticCamera != 0)
                    ? OpticCamera
                    : FPSCamera;

                if (viewCamera == 0)
                    return;

                ActiveCameraPtr = viewCamera;

                ulong viewAddr = viewCamera + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset;
                scatter.PrepareReadValue<Matrix4x4>(viewAddr);

                // 🔵 Always read FOV/Aspect from FPS camera if available
                ulong fovAddr = 0;
                ulong aspectAddr = 0;
                ulong zoomAddr = 0;

                if (FPSCamera != 0)
                {
                    fovAddr = FPSCamera + UnitySDK.UnityOffsets.Camera_FOVOffset;
                    aspectAddr = FPSCamera + UnitySDK.UnityOffsets.Camera_AspectRatioOffset;
                    //zoomAddr   = FPSCamera + UnitySDK.UnityOffsets.Camera_ZoomLevelOffset;
                    //DebugScanCameraFovBlock(FPSCamera);
                    scatter.PrepareReadValue<float>(fovAddr);
                    scatter.PrepareReadValue<float>(aspectAddr);
                    //scatter.PrepareReadValue<float>(zoomAddr);
                }

                scatter.Completed += (sender, s) =>
                {
                    try
                    {
                        if (s.ReadValue<Matrix4x4>(viewAddr, out var vm))
                        {
                            if (!float.IsNaN(vm.M11) && !float.IsInfinity(vm.M11))
                            {
                                _viewMatrix.Update(ref vm);
                                _lastValidMatrix = DateTime.UtcNow;
                            }
                        }

                        // 🔵 Update FOV / Aspect unconditionally (but validate ranges)
                        if (fovAddr != 0 && s.ReadValue<float>(fovAddr, out var fov))
                        {
                            if (fov > 1f && fov < 180f)
                                _fov = fov;
                        }

                        if (aspectAddr != 0 && s.ReadValue<float>(aspectAddr, out var aspect))
                        {
                            if (aspect > 0.1f && aspect < 5f)
                                _aspect = aspect;
                        }
                        //if (zoomAddr != 0 && s.ReadValue<float>(zoomAddr, out var zoom))
                        //{
                        //    if (zoom.IsNormalOrZero() && zoom >= 0f && zoom < 100f)
                        //        _zoomLevel = zoom;
                        //}
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Camera scatter callback error: {ex}");
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in CameraManager OnRealtimeLoop: {ex}");
            }
        }
        private static void DebugScanCameraFovBlock(ulong cameraPtr)
        {
            if (cameraPtr == 0)
            {
                Debug.WriteLine("[CameraManager] DebugScanCameraFovBlock: cameraPtr == 0");
                return;
            }

            const int bytesBefore = 0x40;   // scan 64 bytes before
            const int bytesAfter = 0x80;   // and 128 bytes after
            int step = 4;                   // float stride

            var baseOff = (int)UnitySDK.UnityOffsets.Camera_FOVOffset;

            Debug.WriteLine("────────────────────────────────────────────────────────");
            Debug.WriteLine($"[CameraManager] FOV scan around Camera @ 0x{cameraPtr:X}");
            Debug.WriteLine($"[CameraManager] Current FOV offset guess: 0x{baseOff:X}");
            Debug.WriteLine(" Offset  |   Float Value");
            Debug.WriteLine("────────────────────────────────────────────────────────");

            for (int rel = -bytesBefore; rel <= bytesAfter; rel += step)
            {
                int off = baseOff + rel;
                ulong addr = cameraPtr + (uint)off;

                float value;
                try
                {
                    value = Memory.ReadValue<float>(addr, false);
                }
                catch
                {
                    continue;
                }

                // Filter out totally insane values
                if (float.IsNaN(value) || float.IsInfinity(value))
                    continue;

                // Only log “reasonable” camera-ish floats
                if (value > -360f && value < 360f)
                {
                    Debug.WriteLine($"  0x{off:X4} : {value,8:F3}");
                }
            }

            Debug.WriteLine("────────────────────────────────────────────────────────");
        }

        /// <summary>
        /// Check if camera manager is ready to use (matrices initialized with valid data)
        /// </summary>
        public bool IsInitialized => _matrixInitialized;

        // Zoom factor from sight component (1.0f = unzoomed / base)
        private static float _zoomLevel = 1.0f;
        public static float ZoomLevel => _zoomLevel;

        #region SightComponent Structures

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly struct SightComponent
        {
            [FieldOffset((int)Offsets.SightComponent._template)]
            private readonly ulong pSightInterface;

            [FieldOffset((int)Offsets.SightComponent.ScopesSelectedModes)]
            private readonly ulong pScopeSelectedModes;

            [FieldOffset((int)Offsets.SightComponent.SelectedScope)]
            private readonly int SelectedScope;

            [FieldOffset((int)Offsets.SightComponent.ScopeZoomValue)]
            public readonly float ScopeZoomValue;

            public readonly float GetZoomLevel()
            {
                try
                {
                    using var zoomArray = SightInterface.Zooms;
                    if (SelectedScope >= zoomArray.Count || SelectedScope is < 0 or > 10)
                        return -1.0f;

                    using var selectedScopeModes = UnityArray<int>.Create(pScopeSelectedModes, false);
                    int selectedScopeMode = SelectedScope >= selectedScopeModes.Count ? 0 : selectedScopeModes[SelectedScope];
                    ulong zoomAddr = zoomArray[SelectedScope] + UnityArray<float>.ArrBaseOffset + (uint)selectedScopeMode * 0x4;

                    float zoomLevel = Memory.ReadValue<float>(zoomAddr, false);
                    if (zoomLevel.IsNormalOrZero() && zoomLevel is >= 0f and < 100f)
                    {
                        zoomLevel = _zoomLevel;
                        return zoomLevel;
                    }

                    return -1.0f;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ERROR in GetZoomLevel: {ex}");
                    return -1.0f;
                }
            }

            public readonly SightInterface SightInterface => Memory.ReadValue<SightInterface>(pSightInterface);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly struct SightInterface
        {
            [FieldOffset((int)Offsets.SightInterface.Zooms)]
            private readonly ulong pZooms;

            public readonly UnityArray<ulong> Zooms => UnityArray<ulong>.Create(pZooms, true);
        }

        #endregion

        #region Static Interfaces

        private const int VIEWPORT_TOLERANCE = 800;
        private static readonly Lock _viewportSync = new();

        public static bool EspRunning { get; set; }
        public static Rectangle Viewport { get; private set; }
        public static SKPoint ViewportCenter => new SKPoint(Viewport.Width / 2f, Viewport.Height / 2f);
        public static bool IsScoped { get; private set; }
        public static bool IsADS { get; private set; }

        private static float _fov;
        private static float _aspect;
        private static readonly ViewMatrix _viewMatrix = new();

        public static void UpdateViewportRes()
        {
            //lock (_viewportSync)
            //{
            //    var width = (int)App.Config.ESP.Resolution.Width;
            //    var height = (int)App.Config.ESP.Resolution.Height;

            //    if (width < 640 || height < 480)
            //    {
            //        Debug.WriteLine($"[CameraManager] ESP resolution invalid ({width}x{height}), falling back to 1920x1080");
            //        width = 1920;
            //        height = 1080;
            //    }

            //    Viewport = new Rectangle(0, 0, width, height);
            //    Debug.WriteLine($"[CameraManager] Viewport updated to {width}x{height}");
            //}
        }

        public static bool WorldToScreen(
            ref readonly Vector3 worldPos,
            out SKPoint scrPos,
            bool onScreenCheck = false,
            bool useTolerance = false)
        {
            try
            {
                // Ensure viewport valid
                if (Viewport.Width <= 0 || Viewport.Height <= 0)
                {
                    Debug.WriteLine("[CameraManager] WorldToScreen: viewport was 0x0, calling UpdateViewportRes()");
                    UpdateViewportRes();
                }

                var vm = _viewMatrix;

                float w = Vector3.Dot(vm.Translation, worldPos) + vm.M44;
                if (w < 0.0001f)
                {
                    scrPos = default;
                    return false;
                }

                float x = Vector3.Dot(vm.Right, worldPos) + vm.M14;
                float y = Vector3.Dot(vm.Up, worldPos) + vm.M24;

                // ⬇️ Old behavior: apply FOV scaling when scoped
                if (IsScoped)
                {
                    // _fov is in degrees
                    float angleRadHalf = (MathF.PI / 180f) * _fov * 0.5f;
                    float angleCtg = MathF.Cos(angleRadHalf) / MathF.Sin(angleRadHalf);

                    // Same math as old CameraManagerBase
                    x /= angleCtg * _aspect * 0.5f;
                    y /= angleCtg * 0.5f;
                }

                var center = ViewportCenter;

                scrPos = new SKPoint
                {
                    X = center.X * (1f + x / w),
                    Y = center.Y * (1f - y / w)
                };

                if (onScreenCheck)
                {
                    int left = useTolerance ? Viewport.Left - VIEWPORT_TOLERANCE : Viewport.Left;
                    int right = useTolerance ? Viewport.Right + VIEWPORT_TOLERANCE : Viewport.Right;
                    int top = useTolerance ? Viewport.Top - VIEWPORT_TOLERANCE : Viewport.Top;
                    int bottom = useTolerance ? Viewport.Bottom + VIEWPORT_TOLERANCE : Viewport.Bottom;

                    if (scrPos.X < left || scrPos.X > right || scrPos.Y < top || scrPos.Y > bottom)
                    {
                        scrPos = default;
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in WorldToScreen: {ex}");
                scrPos = default;
                return false;
            }
        }

        public static CameraDebugSnapshot GetDebugSnapshot()
        {
            // Take copies so we don't race on the struct internals
            var right = _viewMatrix.Right;
            var up = _viewMatrix.Up;
            var trans = _viewMatrix.Translation;

            // Normalize basis vectors for debug (and clamp tiny noise to 0)
            right = NormalizeAndClean(right);
            up = NormalizeAndClean(up);

            return new CameraDebugSnapshot
            {
                EspRunning = EspRunning,
                IsADS = IsADS,
                IsScoped = IsScoped,
                FPSCamera = FPSCameraPtr,
                OpticCamera = OpticCameraPtr,
                ActiveCamera = ActiveCameraPtr,
                Fov = _fov,
                Aspect = _aspect,
                M14 = _viewMatrix.M14,
                M24 = _viewMatrix.M24,
                M44 = _viewMatrix.M44,
                RightX = right.X,
                RightY = right.Y,
                RightZ = right.Z,
                UpX = up.X,
                UpY = up.Y,
                UpZ = up.Z,
                TransX = trans.X,
                TransY = trans.Y,
                TransZ = trans.Z,
                ViewportW = Viewport.Width,
                ViewportH = Viewport.Height,
                ZoomLevel = _zoomLevel
            };
        }

        // Debug helper only; not used in actual math
        private static Vector3 NormalizeAndClean(Vector3 v)
        {
            const float minLenSq = 1e-4f;    // ignore near-zero garbage
            const float eps = 1e-3f;         // clamp tiny components to 0

            if (v.LengthSquared() < minLenSq)
                return Vector3.Zero;

            v = Vector3.Normalize(v);

            if (Math.Abs(v.X) < eps) v.X = 0f;
            if (Math.Abs(v.Y) < eps) v.Y = 0f;
            if (Math.Abs(v.Z) < eps) v.Z = 0f;

            return v;
        }

        public readonly struct CameraDebugSnapshot
        {
            public bool EspRunning { get; init; }
            public bool IsADS { get; init; }
            public bool IsScoped { get; init; }
            public ulong FPSCamera { get; init; }
            public ulong OpticCamera { get; init; }
            public ulong ActiveCamera { get; init; }
            public float Fov { get; init; }
            public float Aspect { get; init; }
            public float M14 { get; init; }
            public float M24 { get; init; }
            public float M44 { get; init; }
            public float RightX { get; init; }
            public float RightY { get; init; }
            public float RightZ { get; init; }
            public float UpX { get; init; }
            public float UpY { get; init; }
            public float UpZ { get; init; }
            public float TransX { get; init; }
            public float TransY { get; init; }
            public float TransZ { get; init; }
            public int ViewportW { get; init; }
            public int ViewportH { get; init; }
            public float ZoomLevel { get; init; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetFovMagnitude(SKPoint point)
        {
            return Vector2.Distance(ViewportCenter.AsVector2(), point.AsVector2());
        }
        #endregion
    }
}
