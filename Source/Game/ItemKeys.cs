using System.IO;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game
{
    internal static class ItemKeys
    {
        // Exact serialized item state (DISK-03): ItemValue.Write covers type,
        // quality, durability, modifications, cosmetic mods, metadata, seed and
        // texture. Two stacks merge only when every byte matches.
        public static ItemKey FromItemValue(ItemValue value)
        {
            var stream = MemoryPools.poolMemoryStream.AllocSync(_bReset: true);
            try
            {
                using (var writer = MemoryPools.poolBinaryWriter.AllocSync(_bReset: false))
                {
                    writer.SetBaseStream(stream);
                    value.Write(writer);
                }
                return new ItemKey(stream.ToArray());
            }
            finally
            {
                MemoryPools.poolMemoryStream.FreeSync(stream);
            }
        }

        public static string HexPrefix(ItemKey key)
        {
            byte[] bytes = key.ToArray();
            int shown = System.Math.Min(bytes.Length, 16);
            var hex = new char[shown * 2];
            for (int i = 0; i < shown; i++)
            {
                hex[i * 2] = "0123456789abcdef"[bytes[i] >> 4];
                hex[i * 2 + 1] = "0123456789abcdef"[bytes[i] & 0xF];
            }
            return new string(hex) + "/" + bytes.Length + "B";
        }
    }
}
