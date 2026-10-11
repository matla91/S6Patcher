using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using S6Patcher.Source.Utilities;

namespace S6Patcher.Source.Patcher
{
    internal static class FileDataPatches
    {
        public static byte[] Apply(FileDataEntry Entry, byte[] Content)
        {
            List<byte> Result = [.. Content];
            foreach (var Patch in Entry.Data.OrderByDescending(Item => Item.Key))
            {
                byte[] Data = Patch.Value;
                if (Data == null || Data.Length == 0 || Patch.Key > int.MaxValue)
                    throw new InvalidDataException("Invalid FileData operation.");
                int Offset = (int)Patch.Key;
                int Length;
                switch (Data[0])
                {
                    case 0: // replace, without silently shortening the expected range
                        Length = Data.Length - 1;
                        CheckRange(Offset, Length, Result.Count);
                        Result.RemoveRange(Offset, Length);
                        Result.InsertRange(Offset, Data.Skip(1));
                        break;
                    case 1:
                        CheckRange(Offset, 0, Result.Count);
                        Result.InsertRange(Offset, Data.Skip(1));
                        break;
                    case 2:
                        if (Data.Length != 5) throw new InvalidDataException("Delete operation must contain a uint32 length.");
                        uint Size = BitConverter.ToUInt32(Data, 1);
                        if (Size > int.MaxValue) throw new InvalidDataException("Delete operation is too large.");
                        CheckRange(Offset, (int)Size, Result.Count);
                        Result.RemoveRange(Offset, (int)Size);
                        break;
                    default: throw new InvalidDataException("Unknown FileData operation: " + Data[0]);
                }
            }
            return [.. Result];
        }

        private static void CheckRange(int Offset, int Length, int Count)
        {
            if (Offset < 0 || Length < 0 || Offset > Count - Length)
                throw new InvalidDataException("FileData operation extends outside its source file.");
        }
    }
}
