/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

using System;
using System.Diagnostics;
using Collections.Pooled;
using LoneEftDmaRadar.DMA;

namespace LoneEftDmaRadar.Tarkov.Mono.Collections
{
    /// <summary>
    /// DMA Wrapper for a C# List
    /// Must initialize before use. Must dispose after use.
    /// </summary>
    /// <typeparam name="T">Collection Type</typeparam>
    public sealed class MonoList<T> : PooledMemory<T>
        where T : unmanaged
    {
        public const uint CountOffset = 0x18;
        public const uint ArrOffset = 0x10;
        public const uint ArrStartOffset = 0x20;

        // Conservative upper bound so a bad read doesn't try to allocate something insane.
        private const int MaxCount = 16384;

        private MonoList() : base(0) { }
        private MonoList(int count) : base(count) { }

        private static MonoList<T> CreateEmpty()
        {
            return new MonoList<T>(0);
        }

        /// <summary>
        /// Factory method to create a new <see cref="MonoList{T}"/> instance from a memory address.
        /// </summary>
        /// <param name="addr">Address of the managed List&lt;T&gt; instance in target process.</param>
        /// <param name="useCache">Whether to use the MemDMA read cache.</param>
        /// <returns>A MonoList wrapper (may be empty if addr/count are invalid).</returns>
        public static MonoList<T> Create(ulong addr, bool useCache = true)
        {
            try
            {
                // Basic sanity: null or clearly bogus VA → treat as empty list.
                if (addr == 0 || addr > 0x7FFF_FFFF_FFFFul)
                {
                    return CreateEmpty();
                }

                // Read list.Count from List<T> header.
                int count = LoneEftDmaRadar.DMA.Memory.ReadValue<int>(addr + CountOffset, useCache);

                // Negative or ridiculous? Consider this a bad header and return empty.
                if (count < 0 || count > MaxCount)
                {
                    Debug.WriteLine($"[MonoList<{typeof(T).Name}>] Invalid count={count} @ 0x{addr:X}, returning empty list.");
                    return CreateEmpty();
                }

                // Zero-length list is fine – allocate zero-length backing buffer.
                if (count == 0)
                {
                    return CreateEmpty();
                }

                var list = new MonoList<T>(count);

                try
                {
                    // addr+ArrOffset -> pointer to managed T[]; then skip the array header.
                    ulong itemsBase = LoneEftDmaRadar.DMA.Memory.ReadPtr(addr + ArrOffset, useCache);
                    if (itemsBase == 0 || itemsBase > 0x7FFF_FFFF_FFFFul)
                    {
                        Debug.WriteLine($"[MonoList<{typeof(T).Name}>] Invalid itemsBase=0x{itemsBase:X} for count={count}, returning empty list.");
                        list.Dispose();
                        return CreateEmpty();
                    }

                    ulong listBase = itemsBase + ArrStartOffset;

                    // Fill our backing span from target process memory.
                    LoneEftDmaRadar.DMA.Memory.ReadSpan(listBase, list.Span, useCache);
                    return list;
                }
                catch
                {
                    list.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Fail-safe: on any unexpected error, log and return an empty list
                // so callers like CheckIfScoped() don't explode on transient junk.
                Debug.WriteLine($"[MonoList<{typeof(T).Name}>] Create(0x{addr:X}) FAILED: {ex}");
                return CreateEmpty();
            }
        }
    }
}
