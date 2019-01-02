/* * CJOCh264encoder.cpp
 *
 *  Created on: Aug 17, 2014
 *      Author: Jordi Cenzano (www.jordicenzano.name)
 */

/*
 * CJOCh264encoder.h
 *
 *  Created on: Aug 17, 2014
 *      Author: Jordi Cenzano (www.jordicenzano.name)
 */



//C++ TO C# CONVERTER NOTE: The following #define macro was replaced in-line:
//ORIGINAL LINE: #define BUFFER_SIZE_BYTES (24/8)

//! h264 encoder class
/*!
 It is used to create the h264 compliant stream
 */
using System.Collections.Generic;

public class CJOCh264encoder : CJOCh264bitstream
{

	/**
	 * Allowed sample formats
	 */
	public enum enSampleFormat
	{
		SAMPLE_FORMAT_YUV420p //!< SAMPLE_FORMAT_YUV420p
	}


    public List<byte> m_pOutFile = null;
    public byte[] sps = null;
    public byte[] pps = null;
    public byte[] nal = null;

	/*!Set the used Y macroblock size for I PCM in YUV420p */
	private const int MACROBLOCK_Y_WIDTH = 16;
	private const int MACROBLOCK_Y_HEIGHT = 16;

	/*!Set time base in Hz */
	private const int TIME_SCALE_IN_HZ = 27000000;

	/*!Pointer to pixels */
	private class YUV420p_frame_t
	{
		public byte[] pYCbCr;
	}

	/*! Frame  */
	private class frame_t
	{
		public enSampleFormat sampleformat; //!< Sample format
		public uint nYwidth; //!< Y (luminance) block width in pixels
		public uint nYheight; //!< Y (luminance) block height in pixels
		public uint nCwidth; //!< C (Crominance) block width in pixels
		public uint nCheight; //!< C (Crominance) block height in pixels

		public uint nYmbwidth; //!< Y (luminance) macroblock width in pixels
		public uint nYmbheight; //!< Y (luminance) macroblock height in pixels
		public uint nCmbwidth; //!< Y (Crominance) macroblock width in pixels
		public uint nCmbheight; //!< Y (Crominance) macroblock height in pixels

		public YUV420p_frame_t yuv420pframe = new YUV420p_frame_t(); //!< Pointer to current frame data
		public uint nyuv420pframesize; //!< Size in bytes of yuv420pframe
	}

	/*! The frame var*/
	private frame_t m_frame = new frame_t();

	/*! The frames per second var*/
	private uint m_nFps;

	/*! Number of frames sent to the output */
	private uint m_lNumFramesAdded;



    //! Frees the frame yuv420pframe allocated memory

    //Free the allocated video frame mem
    private void free_video_src_frame()
	{
		if (m_frame.yuv420pframe.pYCbCr != null)
		{
//C++ TO C# CONVERTER TODO TASK: The memory management function 'free' has no equivalent in C#:
//			free(m_frame.yuv420pframe.pYCbCr);
		}

//C++ TO C# CONVERTER TODO TASK: The memory management function 'memset' has no equivalent in C#:
//		memset(m_frame, 0, sizeof(frame_t));
	}

	//! Allocs the frame yuv420pframe memory according to the frame properties

	//Alloc mem to store a video frame
	private void alloc_video_src_frame()
	{
		if (m_frame.yuv420pframe.pYCbCr != null)
		{
			throw new System.Exception("Error: null values in frame");
		}

		uint nYsize = m_frame.nYwidth * m_frame.nYheight;
		uint nCsize = m_frame.nCwidth * m_frame.nCheight;
		m_frame.nyuv420pframesize = nYsize + nCsize + nCsize;

		m_frame.yuv420pframe.pYCbCr = new byte[m_frame.nyuv420pframesize];

		if (m_frame.yuv420pframe.pYCbCr == null)
		{
			throw new System.Exception("Error: memory alloc");
		}
	}

	//! Creates SPS NAL and add it to the output
	/*!
		\param nImW Frame width in pixels
		\param nImH Frame height in pixels
		\param nMbW macroblock width in pixels
		\param nMbH macroblock height in pixels
		\param nFps frames x second (tipical values are: 25, 30, 50, etc)
		\param nSARw Indicates the horizontal size of the sample aspect ratio (tipical values are:1, 4, 16, etc)
		\param nSARh Indicates the vertical size of the sample aspect ratio (tipical values are:1, 3, 9, etc)
	 */

	//Creates and saves the NAL SPS (including VUI) (one per file)
	private void create_sps(uint nImW, uint nImH, uint nMbW, uint nMbH, uint nFps, uint nSARw, uint nSARh)
	{
		add4bytesnoemulationprevention(0x000001); // NAL header
		addbits(0x0, 1); // forbidden_bit
		addbits(0x3, 2); // nal_ref_idc
		addbits(0x7, 5); // nal_unit_type : 7 ( SPS )
		addbits(0x42, 8); // profile_idc = baseline ( 0x42 )
		addbits(0x0, 1); // constraint_set0_flag
		addbits(0x0, 1); // constraint_set1_flag
		addbits(0x0, 1); // constraint_set2_flag
		addbits(0x0, 1); // constraint_set3_flag
		addbits(0x0, 1); // constraint_set4_flag
		addbits(0x0, 1); // constraint_set5_flag
		addbits(0x0, 2); // reserved_zero_2bits /* equal to 0 */
		addbits(0x0a, 8); // level_idc: 3.1 (0x0a)
		addexpgolombunsigned(0); // seq_parameter_set_id
		addexpgolombunsigned(0); // log2_max_frame_num_minus4
		addexpgolombunsigned(0); // pic_order_cnt_type
		addexpgolombunsigned(0); // log2_max_pic_order_cnt_lsb_minus4
		addexpgolombunsigned(0); // max_num_refs_frames
		addbits(0x0, 1); // gaps_in_frame_num_value_allowed_flag

		uint nWinMbs = nImW / nMbW;
		addexpgolombunsigned(nWinMbs - 1); // pic_width_in_mbs_minus_1
		uint nHinMbs = nImH / nMbH;
		addexpgolombunsigned(nHinMbs - 1); // pic_height_in_map_units_minus_1

		addbits(0x1, 1); // frame_mbs_only_flag
		addbits(0x0, 1); // direct_8x8_interfernce
		addbits(0x0, 1); // frame_cropping_flag
//        addbits(0x1, 1); // vui_parameter_present
        addbits(0x0, 1); // vui_parameter_present

		//VUI parameters (AR, timming)
//		addbits(0x1, 1); //aspect_ratio_info_present_flag
//		addbits(0xFF, 8); //aspect_ratio_idc = Extended_SAR

		//AR
//		addbits(nSARw, 16); //sar_width
//		addbits(nSARh, 16); //sar_height

//		addbits(0x0, 1); //overscan_info_present_flag
//		addbits(0x0, 1); //video_signal_type_present_flag
//		addbits(0x0, 1); //chroma_loc_info_present_flag
//		addbits(0x1, 1); //timing_info_present_flag

//		uint nnum_units_in_tick = TIME_SCALE_IN_HZ / (2 * nFps);
//		addbits(nnum_units_in_tick, 32); //num_units_in_tick
//		addbits(TIME_SCALE_IN_HZ, 32); //time_scale
//		addbits(0x1, 1); //fixed_frame_rate_flag

//		addbits(0x0, 1); //nal_hrd_parameters_present_flag
//		addbits(0x0, 1); //vcl_hrd_parameters_present_flag
//		addbits(0x0, 1); //pic_struct_present_flag
//		addbits(0x0, 1); //bitstream_restriction_flag
		//END VUI

//BUG?		addbits(0x0, 1); // frame_mbs_only_flag
		addbits(0x1, 1); // rbsp stop bit

		dobytealign();
	}

	//! Creates PPS NAL and add it to the output

	//Creates and saves the NAL PPS (one per file)
	private void create_pps()
	{
		add4bytesnoemulationprevention(0x000001); // NAL header
		addbits(0x0, 1); // forbidden_bit
		addbits(0x3, 2); // nal_ref_idc
		addbits(0x8, 5); // nal_unit_type : 8 ( PPS )
		addexpgolombunsigned(0); // pic_parameter_set_id
		addexpgolombunsigned(0); // seq_parameter_set_id
		addbits(0x0, 1); // entropy_coding_mode_flag
		addbits(0x0, 1); // bottom_field_pic_order_in frame_present_flag
		addexpgolombunsigned(0); // nun_slices_groups_minus1
		addexpgolombunsigned(0); // num_ref_idx10_default_active_minus
		addexpgolombunsigned(0); // num_ref_idx11_default_active_minus
		addbits(0x0, 1); // weighted_pred_flag
		addbits(0x0, 2); // weighted_bipred_idc
		addexpgolombsigned(0); // pic_init_qp_minus26
		addexpgolombsigned(0); // pic_init_qs_minus26
		addexpgolombsigned(0); // chroma_qp_index_offset
		addbits(0x0, 1); //deblocking_filter_present_flag
		addbits(0x0, 1); // constrained_intra_pred_flag
		addbits(0x0, 1); //redundant_pic_ent_present_flag
		addbits(0x1, 1); // rbsp stop bit

		dobytealign();
	}

	//! Creates Slice NAL and add it to the output
	/*!
		\param lFrameNum number of frame
	 */

	//Creates and saves the NAL SLICE (one per frame)
    //H264 Spec Section 7.3.3 Slice Header Syntax
	private void create_slice_header(uint lFrameNum)
	{
		add4bytesnoemulationprevention(0x000001); // NAL header
		addbits(0x0, 1); // forbidden_bit
		addbits(0x3, 2); // nal_ref_idc
		addbits(0x5, 5); // nal_unit_type : 5 ( Coded slice of an IDR picture  )
		addexpgolombunsigned(0); // first_mb_in_slice
		addexpgolombunsigned(7); // slice_type
		addexpgolombunsigned(0); // pic_param_set_id

        byte cFrameNum = 0; // (byte)(lFrameNum % 16); // H264 Spec says "If the current picture is an IDR picture, frame_num shall be equal to 0. "
                            // Also any maths here must relate to the value of log2_max_frame_num_minus4 in the SPS

        addbits(cFrameNum, 4); // frame_num ( numbits = v = log2_max_frame_num_minus4 + 4)

        // idr_pic_id range is 0..65535. All slices in the same IDR must have the same pic_id. Spec says if there are two
        // IDRs back to back they must have different idr_pic_id values
		uint lidr_pic_id = lFrameNum % 65536;

        addexpgolombunsigned(lidr_pic_id); // idr_pic_id

		addbits(0x0, 4); // pic_order_cnt_lsb (numbits = v = log2_max_fpic_order_cnt_lsb_minus4 + 4)
        // nal_ref_idc != 0. Insert dec_ref_pic_marking
		addbits(0x0, 1); // no_output_of_prior_pics_flag
		addbits(0x0, 1); // long_term_reference_flag

		addexpgolombsigned(0); //slice_qp_delta

		//Probably NOT byte aligned!!!
	}

	//! Creates macroblock header and add it to the output

	//Creates and saves the macroblock header(one per macroblock)
	private void create_macroblock_header()
	{
		addexpgolombunsigned(25); // mb_type (I_PCM)
	}

	//! Creates the slice footer and add it to the output

	//Creates and saves the SLICE footer (one per SLICE)
	private void create_slice_footer()
	{
		addbits(0x1, 1); // rbsp stop bit
	}

	//! Creates SPS NAL and add it to the output
	/*!
		\param nYpos First vertical macroblock pixel inside the frame
		\param nYpos nXpos horizontal macroblock pixel inside the frame
	 */

	//Creates & saves a macroblock (coded INTRA 16x16)
	private void create_macroblock(uint nYpos, uint nXpos)
	{
		uint x;
		uint y;

		create_macroblock_header();

		dobytealign();

		//Y
		uint nYsize = m_frame.nYwidth * m_frame.nYheight;
		for (y = nYpos * m_frame.nYmbheight; y < (nYpos + 1) * m_frame.nYmbheight; y++)
		{
			for (x = nXpos * m_frame.nYmbwidth; x < (nXpos + 1) * m_frame.nYmbwidth; x++)
			{
				addbyte(m_frame.yuv420pframe.pYCbCr[(y * m_frame.nYwidth + x)]);
			}
		}

		//Cb
		uint nCsize = m_frame.nCwidth * m_frame.nCheight;
		for (y = nYpos * m_frame.nCmbheight; y < (nYpos + 1) * m_frame.nCmbheight; y++)
		{
			for (x = nXpos * m_frame.nCmbwidth; x < (nXpos + 1) * m_frame.nCmbwidth; x++)
			{
				addbyte(m_frame.yuv420pframe.pYCbCr[nYsize + (y * m_frame.nCwidth + x)]);
			}
		}

		//Cr
		for (y = nYpos * m_frame.nCmbheight; y < (nYpos + 1) * m_frame.nCmbheight; y++)
		{
			for (x = nXpos * m_frame.nCmbwidth; x < (nXpos + 1) * m_frame.nCmbwidth; x++)
			{
				addbyte(m_frame.yuv420pframe.pYCbCr[nYsize + nCsize + (y * m_frame.nCwidth + x)]);
			}
		}
	}

	//! Constructor
	/*!
		 \param pOutFile The output file pointer
	 */

	//Private functions

	//Contructor
	public CJOCh264encoder(List<byte> pOutFile) : base(pOutFile)
	{
		m_lNumFramesAdded = 0;

//C++ TO C# CONVERTER TODO TASK: The memory management function 'memset' has no equivalent in C#:
		//memset(m_frame, 0, sizeof(frame_t));
		m_nFps = 25;

        m_pOutFile = pOutFile;
	}

	//! Destructor

	//Destructor
	public override void Dispose()
	{
		free_video_src_frame();
		base.Dispose();
	}

	//! Initializes the coder
	/*!
		\param nImW Frame width in pixels
		\param nImH Frame height in pixels
		\param nFps Desired frames per second of the output file (typical values are: 25, 30, 50, etc)
		\param SampleFormat Sample format if the input file. In this implementation only SAMPLE_FORMAT_YUV420p is allowed
		\param nSARw Indicates the horizontal size of the sample aspect ratio (typical values are:1, 4, 16, etc)
		\param nSARh Indicates the vertical size of the sample aspect ratio (typical values are:1, 3, 9, etc)
	*/

	//public functions

	//Initilizes the h264 coder (mini-coder)
	public void IniCoder(uint nImW, uint nImH, uint nImFps, CJOCh264encoder.enSampleFormat SampleFormat, uint nSARw = 1, uint nSARh = 1)
	{
		m_lNumFramesAdded = 0;

		if (SampleFormat != enSampleFormat.SAMPLE_FORMAT_YUV420p)
		{
			throw new System.Exception("Error: SAMPLE FORMAT not allowed. Only yuv420p is allowed in this version");
		}

		free_video_src_frame();

		//Ini vars
		m_frame.sampleformat = SampleFormat;
		m_frame.nYwidth = nImW;
		m_frame.nYheight = nImH;
		if (SampleFormat == enSampleFormat.SAMPLE_FORMAT_YUV420p)
		{
			//Set macroblock Y size
			m_frame.nYmbwidth = MACROBLOCK_Y_WIDTH;
			m_frame.nYmbheight = MACROBLOCK_Y_HEIGHT;

			//Set macroblock C size (in YUV420 is 1/2 of Y)
			m_frame.nCmbwidth = MACROBLOCK_Y_WIDTH / 2;
			m_frame.nCmbheight = MACROBLOCK_Y_HEIGHT / 2;

			//Set C size
			m_frame.nCwidth = m_frame.nYwidth / 2;
			m_frame.nCheight = m_frame.nYheight / 2;

			//In this implementation only picture sizes multiples of macroblock size (16x16) are allowed
			if (((nImW % MACROBLOCK_Y_WIDTH) != 0) || ((nImH % MACROBLOCK_Y_HEIGHT) != 0))
			{
				throw new System.Exception("Error: size not allowed. Only multiples of macroblock are allowed (macroblock size is: 16x16)");
			}
		}
		m_nFps = nImFps;

		//Alloc mem for 1 frame
		alloc_video_src_frame();

		//Create h264 SPS & PPS
		create_sps(m_frame.nYwidth, m_frame.nYheight, m_frame.nYmbwidth, m_frame.nYmbheight, nImFps, nSARw, nSARh);
        close(); // Flush data to the List<byte>
        sps = m_pOutFile.ToArray();
        m_pOutFile.Clear();

		create_pps();
        close(); // Flush data to the List<byte>
        pps = m_pOutFile.ToArray();
        m_pOutFile.Clear();
	}

	//! Returns the frame pointer
	/*!
		\return Frame pointer ready to fill with frame pixels data (the format to fill the data is indicated by SampleFormat parameter when the coder is initialized
	*/

	//Returns the frame pointer to load the video frame
	public byte[] GetFramePtr()
	{
		if (m_frame.yuv420pframe.pYCbCr == null)
		{
			throw new System.Exception("Error: video frame is null (not initialized)");
		}

		return m_frame.yuv420pframe.pYCbCr;
	}

	//! Returns the allocated frame memory in bytes
	/*!
		\return The allocated memory to store the frame data
	*/

	//Returns the the allocated size for video frame
	public uint GetFrameSize()
	{
		return m_frame.nyuv420pframesize;
	}

	//! It codes the frame that is in frame memory a it saves the coded data to disc

	//Codifies & save the video frame (it only uses 16x16 intra PCM -> NO COMPRESSION!)
	public void CodeAndSaveFrame()
	{
        m_pOutFile.Clear();

		//The slice header is not byte aligned, so the first macroblock header is not byte aligned
		create_slice_header(m_lNumFramesAdded);

		//Loop over macroblock size
		uint y;
		uint x;
		for (y = 0; y < m_frame.nYheight / m_frame.nYmbheight; y++)
		{
			for (x = 0; x < m_frame.nYwidth / m_frame.nYmbwidth; x++)
			{
				create_macroblock(y, x);
			}
		}

		create_slice_footer();
		dobytealign();

		m_lNumFramesAdded++;

        // flush
        close();
        nal = m_pOutFile.ToArray();
	}

	//! Returns number of coded frames
	/*!
		\return The number of coded frames
	*/

	//Returns the number of codified frames
	public uint GetSavedFrames()
	{
		return m_lNumFramesAdded;
	}

	//! Flush all data and save the trailing bits

	//Closes the h264 coder saving the last bits in the buffer
	public void CloseCoder()
	{
		close();
	}
}
