using System;
using System.Collections.Generic;

// (c) 2018 Roger Hardiman, RJH Technical Consultancy Ltd
// Simple class to Read and Write bits in a bit stream.
// Data is written to the end of the bit stream and the bit stream can be returned as a Byte Array
// Data can be read from the head of the bit stream
// Example
//    bitstream.AddValue(0xA,4); // Write 4 bit value
//    bitstream.AddValue(0xB,4);
//    bitstream.AddValue(0xC,4);
//    bitstream.AddValue(0xD,4);
//    bitstream.ToArray() -> {0xAB, 0xCD} // Return Byte Array
//    bitstream.Read(8) -> 0xAB  // Read 8 bit value

namespace Rtsp.Utils;

// Very simple bitstream
public class BitStream
{

    private List<byte> data = new(); // List only stores 0 or 1 (one 'bit' per List item)

    // Constructor
    public BitStream()
    {
    }

    public void AddValue(int value, int num_bits)
    {
        // Add each bit to the List
        for (int i = num_bits - 1; i >= 0; i--)
        {
            data.Add((byte)(value >> i & 0x01));
        }
    }

    public void AddHexString(string hex_string)
    {
        char[] hex_chars = hex_string.ToUpper().ToCharArray();
        foreach (char c in hex_chars)
        {
            if (c.Equals('0')) AddValue(0, 4);
            else if (c.Equals('1')) AddValue(1, 4);
            else if (c.Equals('2')) AddValue(2, 4);
            else if (c.Equals('3')) AddValue(3, 4);
            else if (c.Equals('4')) AddValue(4, 4);
            else if (c.Equals('5')) AddValue(5, 4);
            else if (c.Equals('6')) AddValue(6, 4);
            else if (c.Equals('7')) AddValue(7, 4);
            else if (c.Equals('8')) AddValue(8, 4);
            else if (c.Equals('9')) AddValue(9, 4);
            else if (c.Equals('A')) AddValue(10, 4);
            else if (c.Equals('B')) AddValue(11, 4);
            else if (c.Equals('C')) AddValue(12, 4);
            else if (c.Equals('D')) AddValue(13, 4);
            else if (c.Equals('E')) AddValue(14, 4);
            else if (c.Equals('F')) AddValue(15, 4);
        }
    }

    public uint Read(int num_bits)
    {
        // Read and remove items from the front of the list of bits
        if (data.Count < num_bits) return 0;
        uint result = 0;
        for (int i = 0; i < num_bits; i++)
        {
            result <<= 1;
            result += data[0];
            data.RemoveAt(0);
        }
        return result;
    }

    public byte[] ToArray()
    {
        int num_bytes = (int)Math.Ceiling(data.Count / 8.0);
        byte[] array = new byte[num_bytes];
        int ptr = 0;
        int shift = 7;
        for (int i = 0; i < data.Count; i++)
        {
            array[ptr] += (byte)(data[i] << shift);
            if (shift == 0)
            {
                shift = 7;
                ptr++;
            }
            else
            {
                shift--;
            }
        }

        return array;
    }
}