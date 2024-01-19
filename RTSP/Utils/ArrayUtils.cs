﻿namespace Rtsp.Utils;

internal static class ArrayUtils
{
    public static bool IsBytesEquals(byte[] bytes1, int offset1, int count1, byte[] bytes2, int offset2, int count2)
    {
        if (count1 != count2) { return false; }

        for (int i = 0; i < count1; i++)
        {
            if (bytes1[offset1 + i] != bytes2[offset2 + i])
            {
                return false;
            }
        }
        return true;
    }

    public static bool StartsWith(byte[] array, int offset, int count, byte[] pattern)
    {
        int patternLength = pattern.Length;

        if (count < patternLength) { return false; }

        for (int i = 0; i < patternLength; i++, offset++)
        {
            if (array[offset] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    public static bool EndsWith(byte[] array, int offset, int count, byte[] pattern)
    {
        int patternLength = pattern.Length;

        if (count < patternLength) { return false; }

        offset = offset + count - patternLength;

        for (int i = 0; i < patternLength; i++, offset++)
        {
            if (array[offset] != pattern[i])
            {
                return false;
            }
        }
        return true;
    }

    public static int IndexOfBytes(byte[] array, byte[] pattern, int startIndex, int count)
    {
        int patternLength = pattern.Length;

        if (count < patternLength) { return -1; }

        int endIndex = startIndex + count;

        int foundIndex = 0;
        for (; startIndex < endIndex; startIndex++)
        {
            if (array[startIndex] != pattern[foundIndex])
            {
                startIndex -= foundIndex;
                foundIndex = 0;
            }
            else if (++foundIndex == patternLength)
            {
                return startIndex - foundIndex + 1;
            }
        }

        return -1;
    }
}
