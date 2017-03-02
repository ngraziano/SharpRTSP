using System;
using System.Collections.Generic;

/*
 * CJOCh264bitstream.cpp
 *
 *  Created on: Aug 23, 2014
 *      Author: Jordi Cenzano (www.jordicenzano.name)
 */

/*
 * CJOCh264bitstream.h
 *
 *  Created on: Aug 23, 2014
 *      Author: Jordi Cenzano (www.jordicenzano.name)
 */



//! h264 bitstream class
/*!
 It is used to create the h264 bit oriented stream, it contains different functions that helps you to create the h264 compliant stream (bit oriented, exp golomb coder)
 */
public class CJOCh264bitstream : System.IDisposable
{
	private const int BUFFER_SIZE_BITS = 24; //! Buffer size in bits used for emulation prevention
//C++ TO C# CONVERTER NOTE: The following #define macro was replaced in-line:
//ORIGINAL LINE: #define BUFFER_SIZE_BYTES (24/8)

	private const int H264_EMULATION_PREVENTION_BYTE = 0x03; //! Emulation prevention byte


	/*! Buffer  */
	private byte[] m_buffer = new byte[BUFFER_SIZE_BITS];

	/*! Bit buffer index  */
	private int m_nLastbitinbuffer;

	/*! Starting byte indicator  */
	private int m_nStartingbyte;

    /*! Pointer to output file */
    //private FILE m_pOutFile;
    //Byte Array used for output
    private List<byte> m_pOutFile;

	//! Clears the buffer
	private void clearbuffer()
	{
        //C++ TO C# CONVERTER TODO TASK: The memory management function 'memset' has no equivalent in C#:
        //memset(m_buffer, 0, sizeof(byte) * BUFFER_SIZE_BITS);
        System.Array.Clear(m_buffer, 0, BUFFER_SIZE_BITS);
		m_nLastbitinbuffer = 0;
		m_nStartingbyte = 0;
	}

	//! Returns the nNumbit value (1 or 0) of lval
	/*!
	 	 \param lval number to extract the nNumbit value
	 	 \param nNumbit Bit position that we want to know if its 1 or 0 (from 0 to 63)
	 	 \return bit value (1 or 0)
	 */
	private static int getbitnum(uint lval, int nNumbit)
	{
		int lrc = 0;

		uint lmask = (uint) Math.Pow((uint)2,(uint)nNumbit);
		if ((lval & lmask) > 0)
		{
			lrc = 1;
		}

		return lrc;
	}

	//! Adds 1 bit to the end of h264 bitstream
	/*!
		 \param nVal bit to add at the end of h264 bitstream
	 */
	private void addbittostream(int nVal)
	{
		if (m_nLastbitinbuffer >= BUFFER_SIZE_BITS)
		{
			//Must be aligned, no need to do dobytealign();
			savebufferbyte();
		}

		//Use circular buffer of BUFFER_SIZE_BYTES
		int nBytePos = (m_nStartingbyte + (m_nLastbitinbuffer / 8)) % (24 / 8);
		//The first bit to add is on the left
		int nBitPosInByte = 7 - m_nLastbitinbuffer % 8;

		//Get the byte value from buffer
		int nValTmp = m_buffer[nBytePos];

		//Change the bit
		if (nVal > 0)
		{
			nValTmp = (nValTmp | (int) Math.Pow(2,nBitPosInByte));
		}
		else
		{
			nValTmp = (nValTmp & ~((int) Math.Pow(2,nBitPosInByte)));
		}

		//Save the new byte value to the buffer
		m_buffer[nBytePos] = (byte) nValTmp;

		m_nLastbitinbuffer++;
	}

	//! Adds 8 bit to the end of h264 bitstream (it is optimized for byte aligned situations)
	/*!
		 \param nVal byte to add at the end of h264 bitstream (from 0 to 255)
	 */
	private void addbytetostream(int nVal)
	{
		if (m_nLastbitinbuffer >= BUFFER_SIZE_BITS)
		{
			//Must be aligned, no need to do dobytealign();
			savebufferbyte();
		}

		//Used circular buffer of BUFFER_SIZE_BYTES
		int nBytePos = (m_nStartingbyte + (m_nLastbitinbuffer / 8)) % (24 / 8);
		//The first bit to add is on the left
		int nBitPosInByte = 7 - m_nLastbitinbuffer % 8;

		//Check if it is byte aligned
		if (nBitPosInByte != 7)
		{
			throw new System.Exception("Error: inserting not aligment byte");
		}

		//Add all byte to buffer
		m_buffer[nBytePos] = (byte) nVal;

		m_nLastbitinbuffer = m_nLastbitinbuffer + 8;
	}

	//! Save all buffer to file
	/*!
		 \param bemulationprevention Indicates if it will insert the emulation prevention byte or not (when it is needed)
	 */
	private void savebufferbyte(bool bemulationprevention = true)
	{
		bool bemulationpreventionexecuted = false;

		if (m_pOutFile == null)
		{
			throw new System.Exception("Error: out file is NULL");
		}

		//Check if the last bit in buffer is multiple of 8
		if ((m_nLastbitinbuffer % 8) != 0)
		{
			throw new System.Exception("Error: Save to file must be byte aligned");
		}

		if ((m_nLastbitinbuffer / 8) <= 0)
		{
			throw new System.Exception("Error: NO bytes to save");
		}

		if (bemulationprevention == true)
		{
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
			if (    (m_buffer[((m_nStartingbyte + 0) % (24 / 8))] == 0x00)
                &&  (m_buffer[((m_nStartingbyte + 1) % (24 / 8))] == 0x00)
                && ((m_buffer[((m_nStartingbyte + 2) % (24 / 8))] == 0x00)
                       || (m_buffer[((m_nStartingbyte + 2) % (24 / 8))] == 0x01)
                       || (m_buffer[((m_nStartingbyte + 2) % (24 / 8))] == 0x02)
                       || (m_buffer[((m_nStartingbyte + 2) % (24 / 8))] == 0x03)))
			{
				int nbuffersaved = 0;
				byte cEmulationPreventionByte = H264_EMULATION_PREVENTION_BYTE;

				//Save 1st byte
				fwrite(m_buffer[((m_nStartingbyte + nbuffersaved) % (24 / 8))], 1, 1, m_pOutFile);
				nbuffersaved++;

				//Save 2st byte
				fwrite(m_buffer[((m_nStartingbyte + nbuffersaved) % (24 / 8))], 1, 1, m_pOutFile);
				nbuffersaved++;

				//Save emulation prevention byte
				fwrite(cEmulationPreventionByte, 1, 1, m_pOutFile);

				//Save the rest of bytes (usually 1)
				while (nbuffersaved < (24 / 8))
				{
					fwrite(m_buffer[((m_nStartingbyte + nbuffersaved) % (24 / 8))], 1, 1, m_pOutFile);
					nbuffersaved++;
				}

				//All bytes in buffer are saved, so clear the buffer
				clearbuffer();

				bemulationpreventionexecuted = true;
			}
		}

		if (bemulationpreventionexecuted == false)
		{
			//No emulation prevention was used

			//Save the oldest byte in buffer
			fwrite(m_buffer[m_nStartingbyte], 1, 1, m_pOutFile);

			//Move the index
			m_buffer[m_nStartingbyte] = 0;
			m_nStartingbyte++;
			m_nStartingbyte = m_nStartingbyte % (24 / 8);
			m_nLastbitinbuffer = m_nLastbitinbuffer - 8;
		}
	}

	//! Constructor
	/*!
		 \param pOutBinaryFile The output file pointer
	 */
	public CJOCh264bitstream(List<byte> pOutBinaryFile)
	{
		clearbuffer();

//C++ TO C# CONVERTER TODO TASK: C# does not have an equivalent to pointers to variables (in C#, the variable no longer points to the original when the original variable is re-assigned):
//ORIGINAL LINE: m_pOutFile = pOutBinaryFile;
		m_pOutFile = pOutBinaryFile;
	}

	//! Destructor
	public virtual void Dispose()
	{
		close();
	}

	//! Add 4 bytes to h264 bistream without taking into acount the emulation prevention. Used to add the NAL header to the h264 bistream
	/*!
		 \param nVal The 32b value to add
		 \param bDoAlign Indicates if the function will insert 0 in order to create a byte aligned stream before adding nVal 4 bytes to stream. If you try to call this function and the stream is not byte aligned an exception will be thrown
	 */
	public void add4bytesnoemulationprevention(uint nVal, bool bDoAlign = false)
	{
		//Used to add NAL header stream
		//Remember: NAL header is byte oriented

		if (bDoAlign == true)
		{
			dobytealign();
		}

		if ((m_nLastbitinbuffer % 8) != 0)
		{
			throw new System.Exception("Error: Save to file must be byte aligned");
		}

		while (m_nLastbitinbuffer != 0)
		{
			savebufferbyte();
		}

		byte cbyte = (byte)((nVal & 0xFF000000) >> 24);
		fwrite(cbyte, 1, 1, m_pOutFile);

		cbyte = (byte)((nVal & 0x00FF0000) >> 16);
		fwrite(cbyte, 1, 1, m_pOutFile);

		cbyte = (byte)((nVal & 0x0000FF00) >> 8);
		fwrite(cbyte, 1, 1, m_pOutFile);

		cbyte = (byte)(nVal & 0x000000FF);
		fwrite(cbyte, 1, 1, m_pOutFile);
	}

	//! Adds nNumbits of lval to the end of h264 bitstream
	/*!
		 \param nVal value to add at the end of the h264 stream (only the LAST nNumbits will be added)
		 \param nNumbits number of bits of lval that will be added to h264 stream (counting from left)
	 */

	//Public functions

	public void addbits(uint lval, int nNumbits)
	{
		if ((nNumbits <= 0) || (nNumbits > 64))
		{
			throw new System.Exception("Error: numbits must be between 1 ... 64");
		}

		int nBit = 0;
		int n = nNumbits - 1;
		while (n >= 0)
		{
			nBit = getbitnum(lval, n);
			n--;

			addbittostream(nBit);
		}
	}

	//! Adds lval to the end of h264 bitstream using exp golomb coding for unsigned values
	/*!
		 \param nVal value to add at the end of the h264 stream
	 */
	public void addexpgolombunsigned(uint lval)
	{
		//it implements unsigned exp golomb coding

		uint lvalint = lval + 1;
		int nnumbits = (int)(Math.Log(lvalint,2) + 1);

		for (int n = 0; n < (nnumbits - 1); n++)
		{
			addbits(0, 1);
		}

		addbits(lvalint, nnumbits);
	}

	//! Adds lval to the end of h264 bitstream using exp golomb coding for signed values
	/*!
		 \param nVal value to add at the end of the h264 stream
	 */
	public void addexpgolombsigned(int lval)
	{
		//it implements a signed exp golomb coding

		uint lvalint = (uint)(Math.Abs(lval) * 2 - 1);
		if (lval <= 0)
		{
			lvalint = (uint)(2 * Math.Abs(lval));
		}

		addexpgolombunsigned(lvalint);
	}

	//! Adds 0 to the end of h264 bistream in order to leave a byte aligned stream (It will insert seven 0 maximum)
	public void dobytealign()
	{
		//Check if the last bit in buffer is multiple of 8
		int nr = m_nLastbitinbuffer % 8;
		if ((nr % 8) != 0)
		{
			m_nLastbitinbuffer = m_nLastbitinbuffer + (8 - nr);
		}
	}

	//! Adds cByte (8 bits) to the end of h264 bitstream. This function it is optimized in byte aligned streams.
	/*!
		 \param cByte value to add at the end of the h264 stream (from 0 to 255)
	 */
	public void addbyte(byte cByte)
	{
		//Byte alignment optimization
		if ((m_nLastbitinbuffer % 8) == 0)
		{
			addbytetostream(cByte);
		}
		else
		{
			addbits(cByte, 8);
		}
	}

	//! Close the h264 stream saving to disk the last remaing bits in buffer
	public void close()
	{
		//Flush the data in stream buffer

		dobytealign();

		while (m_nLastbitinbuffer != 0)
		{
			savebufferbyte();
		}
	}

    // 'writing' to memory
    private void fwrite(byte b, int x, int y, List<byte>data)
    {
        data.Add(b);
    }
}
