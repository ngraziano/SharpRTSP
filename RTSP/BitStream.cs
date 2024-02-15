using System;
using System.Collections.Generic;
using System.Linq;

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

namespace Rtsp
{
    // Very simple bitstream
    public class BitStream
    {
        /// <summary>
        /// List only stores 0 or 1 (one 'bit' per List item)
        /// </summary>
        private readonly List<byte> data = [];

        public void AddValue(int value, int num_bits)
        {
            // Add each bit to the List
            for (int i = num_bits - 1; i >= 0; i--)
            {
                data.Add((byte)((value >> i) & 0x01));
            }
        }

        public void AddHexString(string hexString)
        {
            foreach (char c in hexString)
            {
                var value = c switch
                {
                    >= 'a' and <= 'f' => c - 'a' + 10,
                    >= 'A' and <= 'F' => c - 'A' + 10,
                    >= '0' and <= '9' => c - '0',
                    _ => throw new ArgumentException("Invalid hex character", nameof(hexString)),
                };
                AddValue(value, 4);
            }
        }

        public int Read(int num_bits)
        {
            // Read and remove items from the front of the list of bits
            if (data.Count < num_bits)
            {
                throw new InvalidOperationException("Not enough bits to read");
            }

            int result = data
                .Take(num_bits)
                .Aggregate(0, (agg, value) => (agg << 1) + value);
            data.RemoveRange(0, num_bits);

            return result;
        }

        public byte[] ToArray()
        {
            // number of byte rounded up
            int num_bytes = (data.Count + 7) / 8;
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
}