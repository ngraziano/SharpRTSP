namespace Rtsp
{
    internal static class ArrayUtils
    {
        public static bool IsBytesEquals(byte[] bytes1, int offset1, int count1, byte[] bytes2, int offset2, int count2)
        {
            if (count1 != count2)
                return false;

            for (int i = 0; i < count1; i++)
                if (bytes1[offset1 + i] != bytes2[offset2 + i])
                    return false;

            return true;
        }
    }

}
