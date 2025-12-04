using System;
using LoneEftDmaRadar.DMA;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.DMA.ScatterAPI
{
    /// <summary>
    /// Write-only scatter handle used by MemWrite features.
    /// Backed by a single VmmScatter instance.
    /// </summary>
    public sealed class ScatterWriteHandle : IDisposable
    {
        private readonly VmmScatter _scatter;
        private bool _disposed;

        /// <summary>
        /// Optional callbacks run after the scatter has executed successfully.
        /// Old InfStamina used this for logging.
        /// </summary>
        public event Action Callbacks;

        /// <summary>
        /// Create a write-only ScatterWriteHandle for the current process.
        /// Uses MemDMA.CreateScatter(VmmFlags.MEMWRITE).
        /// </summary>
        public static ScatterWriteHandle Create()
        {
            // Memory is your global MemDMA instance (already used everywhere).
            var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);
            return new ScatterWriteHandle(scatter);
        }

        /// <summary>
        /// Internal ctor from a pre-created VmmScatter (if you ever want to reuse).
        /// </summary>
        public ScatterWriteHandle(VmmScatter scatter)
        {
            _scatter = scatter ?? throw new ArgumentNullException(nameof(scatter));
        }

        /// <summary>
        /// Add a value write to this scatter.
        /// Mirrors the old AddValueEntry(address, value) semantics.
        /// </summary>
        public void AddValueEntry<T>(ulong address, T value) where T : unmanaged
        {
            ThrowIfDisposed();
            _scatter.PrepareWriteValue(address, value);
        }

        /// <summary>
        /// Execute all prepared writes and invoke callbacks.
        /// Normally called once per memwrite tick in MemWritesManager.
        /// </summary>
        public void Execute()
        {
            ThrowIfDisposed();
            _scatter.Execute();
            Callbacks?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScatterWriteHandle));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _scatter.Dispose();
        }
    }
}
