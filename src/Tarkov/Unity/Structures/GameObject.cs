using VmmSharpEx.Extensions;

namespace LoneEftDmaRadar.Tarkov.Unity.Structures
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct GameObject
    {
        [FieldOffset(0x08)]
        public readonly int InstanceID;

        [FieldOffset((int)UnitySDK.UnityOffsets.GameObject_ObjectClassOffset)]
        public readonly ulong ObjectClass; // m_Object

        [FieldOffset((int)UnitySDK.UnityOffsets.GameObject_ComponentsOffset)]
        public readonly ComponentArray Components;

        [FieldOffset((int)UnitySDK.UnityOffsets.GameObject_NameOffset)]
        public readonly ulong NamePtr;

        public string GetName(int maxLen = 128, bool useCache = true)
        {
            if (!NamePtr.IsValidVA())
                return string.Empty;

            return Memory.ReadUtf8String(NamePtr, maxLen, useCache) ?? string.Empty;
        }

        /// <summary>
        /// Find component by class name and RETURN OBJECTCLASS POINTER
        /// (matches old Mono behaviour).
        /// </summary>
        public ulong GetComponent(string className, bool useCache = true)
        {
            if (string.IsNullOrWhiteSpace(className))
                return 0;

            var componentArr = Components;
            if (!componentArr.ArrayBase.IsValidVA() || componentArr.Size == 0)
                return 0;

            int count = (int)Math.Min(componentArr.Size, 0x400); // safety cap
            var entries = new ComponentArray.Entry[count];

            try
            {
                Memory.ReadSpan(componentArr.ArrayBase, entries);
            }
            catch
            {
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                var compPtr = entries[i].Component;
                if (!compPtr.IsValidVA())
                    continue;

                ulong objectClass;
                try
                {
                    objectClass = Memory.ReadPtr(
                        compPtr + UnitySDK.UnityOffsets.Component_ObjectClassOffset,
                        useCache);
                }
                catch
                {
                    continue;
                }

                if (!objectClass.IsValidVA())
                    continue;

                string name;
                try
                {
                    name = LoneEftDmaRadar.Tarkov.Unity.Structures.ObjectClass.ReadName(objectClass, 128, useCache);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(name))
                    continue;

                if (name.Equals(className, StringComparison.OrdinalIgnoreCase))
                    return objectClass; // <<<< return objectClass, NOT compPtr
            }

            return 0;
        }

        public static ulong GetComponent(ulong gameObjectPtr, string className, bool useCache = true)
        {
            if (!gameObjectPtr.IsValidVA())
                return 0;

            var go = Memory.ReadValue<GameObject>(gameObjectPtr, useCache);
            return go.GetComponent(className, useCache);
        }
    }
}
