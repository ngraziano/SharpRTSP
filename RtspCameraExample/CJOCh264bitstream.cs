using System;
using System.IO;

namespace RtspCameraExample
{
    /*
     * adapted from CJOCh264bitstream.cpp CJOCh264bitstream.h
     *
     *  Created on: Aug 23, 2014
     *      Author: Jordi Cenzano (www.jordicenzano.name)
     */

    /// <summary>
    ///  It is used to create the h264 bit oriented stream, it contains different functions that helps you to create the h264 compliant stream (bit oriented, exp golomb coder)
    /// </summary>
    public class CJOCh264bitstream : IDisposable
    {
        /// <summary>
        /// Buffer size in bits used for emulation prevention
        /// </summary>
        private const int BUFFER_SIZE_BITS = 24;
        private int buffer;

        /// <summary>
        /// Emulation prevention byte
        /// </summary>
        private const int H264_EMULATION_PREVENTION_BYTE = 0x03;


        /// <summary>
        ///  Bit buffer index
        /// </summary>
        private int nLastBitInBuffer;

        private bool disposedValue;
        private readonly Stream stream;

        public CJOCh264bitstream(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            ClearBuffer();
        }

        /// <summary>
        /// Clears the buffer
        /// </summary>
        private void ClearBuffer()
        {
            buffer = 0;
            nLastBitInBuffer = 0;
        }

        /// <summary>
        /// Returns the nNumbit value (1 or 0) of lval
        /// </summary>
        /// <param name="lval">lval number to extract the nNumbit value</param>
        /// <param name="nNumbit">nNumbit Bit position that we want to know if its 1 or 0 (from 0 to 63)</param>
        /// <returns>bit value (1 or 0)</returns>
        private static int GetBitNum(uint lval, int nNumbit) => (lval & 1U << nNumbit) == 0 ? 0 : 1;

        /// <summary>
        /// Adds 1 bit to the end of h264 bitstream
        /// </summary>
        /// <param name="nVal">nVal bit to add at the end of h264 bitstream</param>
        private void AddBitToStream(int nVal)
        {
            if (nLastBitInBuffer >= BUFFER_SIZE_BITS)
            {
                //Must be aligned, no need to do dobytealign();
                SaveBufferByte();
            }

            //Use buffer of BUFFER_SIZE_BITS
            int nBytePos = nLastBitInBuffer / 8;
            //The first bit to add is on the left
            int nBitPosInByte = 7 - (nLastBitInBuffer % 8);

            //Get the byte value from buffer
            int nValTmp = BufferAt(nBytePos);

            //Change the bit
            if (nVal > 0)
            {
                nValTmp |= 1 << nBitPosInByte;
            }
            else
            {
                nValTmp &= ~(1 << nBitPosInByte);
            }

            //Save the new byte value to the buffer
            SetBufferAt(nBytePos, (byte)nValTmp);

            nLastBitInBuffer++;
        }

        //! 
        /*!
             \param 
         */
        /// <summary>
        /// Adds 8 bit to the end of h264 bitstream (it is optimized for byte aligned situations)
        /// </summary>
        /// <param name="nVal">nVal byte to add at the end of h264 bitstream (from 0 to 255)</param>
        /// <exception cref="InvalidOperationException">If add when not aligned</exception>
        private void AddByteToStream(int nVal)
        {
            if (nLastBitInBuffer >= BUFFER_SIZE_BITS)
            {
                //Must be aligned, no need to do dobytealign();
                SaveBufferByte();
            }

            //Used circular buffer of BUFFER_SIZE_BYTES
            int nBytePos = (nLastBitInBuffer / 8);
            //The first bit to add is on the left
            int nBitPosInByte = 7 - (nLastBitInBuffer % 8);

            //Check if it is byte aligned
            if (nBitPosInByte != 7)
            {
                throw new InvalidOperationException("Error: inserting not aligment byte");
            }

            //Add all byte to buffer
            SetBufferAt(nBytePos, (byte)nVal);

            nLastBitInBuffer += 8;
        }

        /// <summary>
        /// Save all buffer to file
        /// </summary>
        private void SaveBufferByte()
        {
            //Check if the last bit in buffer is multiple of 8
            if ((nLastBitInBuffer % 8) != 0)
            {
                throw new Exception("Error: Save to file must be byte aligned");
            }

            if ((nLastBitInBuffer / 8) <= 0)
            {
                throw new Exception("Error: NO bytes to save");
            }

            //Emulation prevention will be used:
            /*As per h.264 spec,
            rbsp_data shouldn't contain
                    - 0x 00 00 00
                    - 0x 00 00 01
                    - 0x 00 00 02
                    - 0x 00 00 03

            rbsp_data shall be in the following way
                    - 0x 00 00 03 00
                    - 0x 00 00 03 01
                    - 0x 00 00 03 02
                    - 0x 00 00 03 03
            */

            //Check if emulation prevention is needed (emulation prevention is byte align defined)
            if ((BufferAt(0) == 0x00)
                && (BufferAt(1) == 0x00)
                && ((BufferAt(1) == 0x00) || (BufferAt(2) == 0x01) || (BufferAt(2) == 0x02) || (BufferAt(2) == 0x03)))
            {
                //Save 1st byte
                stream.WriteByte(PopByteInBuffer());
                //Save 2st byte
                stream.WriteByte(PopByteInBuffer());
                //Save emulation prevention byte
                stream.WriteByte(H264_EMULATION_PREVENTION_BYTE);
                //Save the rest of bytes (usually 1)
                stream.WriteByte(PopByteInBuffer());
                //All bytes in buffer are saved, so clear the buffer
                ClearBuffer();
            }
            else
            {
                //No emulation prevention was used
                //Save the oldest byte in buffer
                stream.WriteByte(PopByteInBuffer());
            }
        }

        /// <summary>
        /// Add 4 bytes to h264 bistream without taking into acount the emulation prevention. Used to add the NAL header to the h264 bistream
        /// </summary>
        /// <param name="value">The 32b value to add</param>
        /// <param name="doAlign">Indicates if the function will insert 0 in order to create a byte aligned stream before adding value 4 bytes to stream. If you try to call this function and the stream is not byte aligned an exception will be thrown</param>
        /// <exception cref="Exception"></exception>
        public void Add4BytesNoEmulationPrevention(uint value, bool doAlign = false)
        {
            ObjectDisposedException.ThrowIf(disposedValue, GetType());

            //Used to add NAL header stream
            //Remember: NAL header is byte oriented


            if (doAlign)
            {
                DoByteAlign();
            }

            if ((nLastBitInBuffer % 8) != 0)
            {
                throw new Exception("Error: Save to file must be byte aligned");
            }

            while (nLastBitInBuffer != 0)
            {
                SaveBufferByte();
            }

            byte cbyte = (byte)((value & 0xFF000000) >> 24);
            stream.WriteByte(cbyte);

            cbyte = (byte)((value & 0x00FF0000) >> 16);
            stream.WriteByte(cbyte);

            cbyte = (byte)((value & 0x0000FF00) >> 8);
            stream.WriteByte(cbyte);

            cbyte = (byte)(value & 0x000000FF);
            stream.WriteByte(cbyte);
        }

        /// <summary>
        ///  Adds nNumbits of lval to the end of h264 bitstream
        /// </summary>
        /// <param name="lval">nVal value to add at the end of the h264 stream (only the LAST nNumbits will be added)</param>
        /// <param name="nNumbits">nNumbits number of bits of lval that will be added to h264 stream (counting from left)</param>
        /// <exception cref="ArgumentOutOfRangeException">if nNumbits is too large</exception>
        public void AddBits(uint lval, int nNumbits)
        {
            ObjectDisposedException.ThrowIf(disposedValue, GetType());

            if ((nNumbits <= 0) || (nNumbits > 64))
            {
                throw new ArgumentOutOfRangeException(nameof(nNumbits), "Error: numbits must be between 1 and 64");
            }

            int n = nNumbits - 1;
            while (n >= 0)
            {
                int nBit = GetBitNum(lval, n);
                n--;

                AddBitToStream(nBit);
            }
        }

        /// <summary>
        /// Adds lval to the end of h264 bitstream using exp golomb coding for unsigned values
        /// </summary>
        /// <param name="lval">value to add at the end of the h264 stream</param>
        public void AddExpGolombUnsigned(uint lval)
        {
            ObjectDisposedException.ThrowIf(disposedValue, GetType());

            //it implements unsigned exp golomb coding
            uint lvalint = lval + 1;
            int nnumbits = (int)(Math.Log(lvalint, 2) + 1);

            for (int n = 0; n < (nnumbits - 1); n++)
            {
                AddBits(0, 1);
            }
            AddBits(lvalint, nnumbits);
        }

        /// <summary>
        /// Adds lval to the end of h264 bitstream using exp golomb coding for signed values
        /// </summary>
        /// <param name="lval">value to add at the end of the h264 stream</param>
        public void AddExpGolombSigned(int lval)
        {
            ObjectDisposedException.ThrowIf(disposedValue, GetType());

            //it implements a signed exp golomb coding

            uint lvalint = lval <= 0 ?
                (uint)(2 * Math.Abs(lval)) :
                (uint)((2 * Math.Abs(lval)) - 1);

            AddExpGolombUnsigned(lvalint);
        }

        //! Adds 0 to the end of h264 bistream in order to leave a byte aligned stream (It will insert seven 0 maximum)
        public void DoByteAlign()
        {
            ObjectDisposedException.ThrowIf(disposedValue, GetType());

            //Check if the last bit in buffer is multiple of 8
            int nr = nLastBitInBuffer % 8;
            if (nr != 0)
            {
                nLastBitInBuffer += (8 - nr);
            }
        }

        /// <summary>
        /// Adds cByte (8 bits) to the end of h264 bitstream. This function it is optimized in byte aligned streams.
        /// </summary>
        /// <param name="cByte">value to add at the end of the h264 stream (from 0 to 255)</param>
        public void AddByte(byte cByte)
        {
            ObjectDisposedException.ThrowIf(disposedValue, GetType());

            //Byte alignment optimization
            if ((nLastBitInBuffer % 8) == 0)
            {
                AddByteToStream(cByte);
            }
            else
            {
                AddBits(cByte, 8);
            }
        }

        //! Close the h264 stream saving to disk the last remaing bits in buffer
        public void Flush()
        {
            //Flush the data in stream buffer

            DoByteAlign();

            while (nLastBitInBuffer != 0)
            {
                SaveBufferByte();
            }
        }

        private byte BufferAt(int n)
        {
            return (byte)(buffer >> (n * 8) & 0xFF);
        }

        private void SetBufferAt(int n, byte val)
        {
            buffer &= ~(0xFF << (n * 8));
            buffer |= val << (n * 8);
        }

        private byte PopByteInBuffer()
        {
            var r = buffer & 0xFF;
            buffer >>= 8;
            nLastBitInBuffer -= 8;
            return (byte)r;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Flush();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(bool disposing)'
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}