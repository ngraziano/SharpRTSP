/* * Adapted from CJOCh264encoder.cpp and CJOCh264encoder.h
 *
 *  Created on: Aug 17, 2014
 *      Author: Jordi Cenzano (www.jordicenzano.name)
 */

using System;
using System.IO;

namespace RtspCameraExample
{
    /// <summary>
    /// It is used to create the h264 compliant stream
    /// </summary>
    public class CJOCh264encoder : IDisposable
    {
        /**
         * Allowed sample formats
         */
        public enum SampleFormat
        {
            SAMPLE_FORMAT_YUV420p //!< SAMPLE_FORMAT_YUV420p
        }

        public byte[]? sps;
        public byte[]? pps;
        public byte[]? nal;

        /*!Set the used Y macroblock size for I PCM in YUV420p */
        private const int MACROBLOCK_Y_WIDTH = 16;
        private const int MACROBLOCK_Y_HEIGHT = 16;

        private class Frame
        {
            public SampleFormat sampleformat; //!< Sample format
            public int nYwidth; //!< Y (luminance) block width in pixels
            public int nYheight; //!< Y (luminance) block height in pixels
            public int nCwidth; //!< C (Crominance) block width in pixels
            public int nCheight; //!< C (Crominance) block height in pixels

            public int nYmbwidth; //!< Y (luminance) macroblock width in pixels
            public int nYmbheight; //!< Y (luminance) macroblock height in pixels
            public int nCmbwidth; //!< Y (Crominance) macroblock width in pixels
            public int nCmbheight; //!< Y (Crominance) macroblock height in pixels
        }

        /*! The frame var*/
        private readonly Frame frame = new();

        /*! The frames per second var*/
        private uint m_nFps;

        /*! Number of frames sent to the output */
        private uint m_lNumFramesAdded;
        private bool disposedValue;
        private readonly MemoryStream baseStream = new();
        private readonly CJOCh264bitstream stream;

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
        private void CreateSps(int nImW, int nImH, int nMbW, int nMbH, uint nFps, uint nSARw, uint nSARh)
        {
            stream.Add4BytesNoEmulationPrevention(0x000001); // NAL header
            stream.AddBits(0x0, 1); // forbidden_bit
            stream.AddBits(0x3, 2); // nal_ref_idc
            stream.AddBits(0x7, 5); // nal_unit_type : 7 ( SPS )
            stream.AddBits(0x42, 8); // profile_idc = baseline ( 0x42 )
            stream.AddBits(0x0, 1); // constraint_set0_flag
            stream.AddBits(0x0, 1); // constraint_set1_flag
            stream.AddBits(0x0, 1); // constraint_set2_flag
            stream.AddBits(0x0, 1); // constraint_set3_flag
            stream.AddBits(0x0, 1); // constraint_set4_flag
            stream.AddBits(0x0, 1); // constraint_set5_flag
            stream.AddBits(0x0, 2); // reserved_zero_2bits /* equal to 0 */
            stream.AddBits(0x0a, 8); // level_idc: 3.1 (0x0a)
            stream.AddExpGolombUnsigned(0); // seq_parameter_set_id
            stream.AddExpGolombUnsigned(0); // log2_max_frame_num_minus4
            stream.AddExpGolombUnsigned(0); // pic_order_cnt_type
            stream.AddExpGolombUnsigned(0); // log2_max_pic_order_cnt_lsb_minus4
            stream.AddExpGolombUnsigned(0); // max_num_refs_frames
            stream.AddBits(0x0, 1); // gaps_in_frame_num_value_allowed_flag

            int nWinMbs = nImW / nMbW;
            stream.AddExpGolombUnsigned((uint)nWinMbs - 1); // pic_width_in_mbs_minus_1
            int nHinMbs = nImH / nMbH;
            stream.AddExpGolombUnsigned((uint)nHinMbs - 1); // pic_height_in_map_units_minus_1

            stream.AddBits(0x1, 1); // frame_mbs_only_flag
            stream.AddBits(0x0, 1); // direct_8x8_interfernce
            stream.AddBits(0x0, 1); // frame_cropping_flag
                                    //        addbits(0x1, 1); // vui_parameter_present
            stream.AddBits(0x0, 1); // vui_parameter_present

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
            stream.AddBits(0x1, 1); // rbsp stop bit

            stream.DoByteAlign();
        }

        //! Creates PPS NAL and add it to the output

        //Creates and saves the NAL PPS (one per file)
        private void CreatePPS()
        {
            stream.Add4BytesNoEmulationPrevention(0x000001); // NAL header
            stream.AddBits(0x0, 1); // forbidden_bit
            stream.AddBits(0x3, 2); // nal_ref_idc
            stream.AddBits(0x8, 5); // nal_unit_type : 8 ( PPS )
            stream.AddExpGolombUnsigned(0); // pic_parameter_set_id
            stream.AddExpGolombUnsigned(0); // seq_parameter_set_id
            stream.AddBits(0x0, 1); // entropy_coding_mode_flag
            stream.AddBits(0x0, 1); // bottom_field_pic_order_in frame_present_flag
            stream.AddExpGolombUnsigned(0); // nun_slices_groups_minus1
            stream.AddExpGolombUnsigned(0); // num_ref_idx10_default_active_minus
            stream.AddExpGolombUnsigned(0); // num_ref_idx11_default_active_minus
            stream.AddBits(0x0, 1); // weighted_pred_flag
            stream.AddBits(0x0, 2); // weighted_bipred_idc
            stream.AddExpGolombSigned(0); // pic_init_qp_minus26
            stream.AddExpGolombSigned(0); // pic_init_qs_minus26
            stream.AddExpGolombSigned(0); // chroma_qp_index_offset
            stream.AddBits(0x0, 1); //deblocking_filter_present_flag
            stream.AddBits(0x0, 1); // constrained_intra_pred_flag
            stream.AddBits(0x0, 1); //redundant_pic_ent_present_flag
            stream.AddBits(0x1, 1); // rbsp stop bit

            stream.DoByteAlign();
        }

        //! Creates Slice NAL and add it to the output
        /*!
            \param lFrameNum number of frame
         */

        //Creates and saves the NAL SLICE (one per frame)
        //H264 Spec Section 7.3.3 Slice Header Syntax
        private void CreateSliceHeader(uint lFrameNum)
        {
            stream.Add4BytesNoEmulationPrevention(0x000001); // NAL header
            stream.AddBits(0x0, 1); // forbidden_bit
            stream.AddBits(0x3, 2); // nal_ref_idc
            stream.AddBits(0x5, 5); // nal_unit_type : 5 ( Coded slice of an IDR picture  )
            stream.AddExpGolombUnsigned(0); // first_mb_in_slice
            stream.AddExpGolombUnsigned(7); // slice_type
            stream.AddExpGolombUnsigned(0); // pic_param_set_id

            byte cFrameNum = 0; // (byte)(lFrameNum % 16); // H264 Spec says "If the current picture is an IDR picture, frame_num shall be equal to 0. "
                                // Also any maths here must relate to the value of log2_max_frame_num_minus4 in the SPS

            stream.AddBits(cFrameNum, 4); // frame_num ( numbits = v = log2_max_frame_num_minus4 + 4)

            // idr_pic_id range is 0..65535. All slices in the same IDR must have the same pic_id. Spec says if there are two
            // IDRs back to back they must have different idr_pic_id values
            uint lidr_pic_id = lFrameNum % 65536;

            stream.AddExpGolombUnsigned(lidr_pic_id); // idr_pic_id

            stream.AddBits(0x0, 4); // pic_order_cnt_lsb (numbits = v = log2_max_fpic_order_cnt_lsb_minus4 + 4)
                                    // nal_ref_idc != 0. Insert dec_ref_pic_marking
            stream.AddBits(0x0, 1); // no_output_of_prior_pics_flag
            stream.AddBits(0x0, 1); // long_term_reference_flag

            stream.AddExpGolombSigned(0); //slice_qp_delta

            //Probably NOT byte aligned!!!
        }

        //! Creates macroblock header and add it to the output

        //Creates and saves the macroblock header(one per macroblock)
        private void CreateMacroblockHeader()
        {
            stream.AddExpGolombUnsigned(25); // mb_type (I_PCM)
        }

        //! Creates the slice footer and add it to the output

        //Creates and saves the SLICE footer (one per SLICE)
        private void CreateSliceFooter()
        {
            stream.AddBits(0x1, 1); // rbsp stop bit
        }

        //! Creates SPS NAL and add it to the output
        /*!
            \param nYpos First vertical macroblock pixel inside the frame
            \param nYpos nXpos horizontal macroblock pixel inside the frame
         */

        //Creates & saves a macroblock (coded INTRA 16x16)
        private void CreateMacroblock(int nYpos, int nXpos, Span<byte> frameBuffer)
        {
            CreateMacroblockHeader();

            stream.DoByteAlign();

            //Y
            int nYsize = frame.nYwidth * frame.nYheight;
            for (int y = nYpos * frame.nYmbheight; y < (nYpos + 1) * frame.nYmbheight; y++)
            {
                for (int x = nXpos * frame.nYmbwidth; x < (nXpos + 1) * frame.nYmbwidth; x++)
                {
                    stream.AddByte(frameBuffer[y * frame.nYwidth + x]);
                }
            }

            //Cb
            int nCsize = frame.nCwidth * frame.nCheight;
            for (int y = nYpos * frame.nCmbheight; y < (nYpos + 1) * frame.nCmbheight; y++)
            {
                for (int x = nXpos * frame.nCmbwidth; x < (nXpos + 1) * frame.nCmbwidth; x++)
                {
                    stream.AddByte(frameBuffer[nYsize + (y * frame.nCwidth + x)]);
                }
            }

            //Cr
            for (int y = nYpos * frame.nCmbheight; y < (nYpos + 1) * frame.nCmbheight; y++)
            {
                for (int x = nXpos * frame.nCmbwidth; x < (nXpos + 1) * frame.nCmbwidth; x++)
                {
                    stream.AddByte(frameBuffer[nYsize + nCsize + (y * frame.nCwidth + x)]);
                }
            }
        }

        public CJOCh264encoder()
        {
            m_lNumFramesAdded = 0;
            stream = new CJOCh264bitstream(baseStream);
            m_nFps = 25;
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
        public void IniCoder(int nImW, int nImH, uint nImFps, SampleFormat sampleFormat, uint nSARw = 1, uint nSARh = 1)
        {
            m_lNumFramesAdded = 0;

            if (sampleFormat != SampleFormat.SAMPLE_FORMAT_YUV420p)
            {
                throw new ArgumentException("Error: SAMPLE FORMAT not allowed. Only yuv420p is allowed in this version", nameof(sampleFormat));
            }

            //Ini vars
            frame.sampleformat = sampleFormat;
            frame.nYwidth = nImW;
            frame.nYheight = nImH;
            if (sampleFormat == SampleFormat.SAMPLE_FORMAT_YUV420p)
            {
                //Set macroblock Y size
                frame.nYmbwidth = MACROBLOCK_Y_WIDTH;
                frame.nYmbheight = MACROBLOCK_Y_HEIGHT;

                //Set macroblock C size (in YUV420 is 1/2 of Y)
                frame.nCmbwidth = MACROBLOCK_Y_WIDTH / 2;
                frame.nCmbheight = MACROBLOCK_Y_HEIGHT / 2;

                //Set C size
                frame.nCwidth = frame.nYwidth / 2;
                frame.nCheight = frame.nYheight / 2;

                //In this implementation only picture sizes multiples of macroblock size (16x16) are allowed
                if (((nImW % MACROBLOCK_Y_WIDTH) != 0) || ((nImH % MACROBLOCK_Y_HEIGHT) != 0))
                {
                    throw new Exception("Error: size not allowed. Only multiples of macroblock are allowed (macroblock size is: 16x16)");
                }
            }
            m_nFps = nImFps;

            //Create h264 SPS & PPS
            CreateSps(frame.nYwidth, frame.nYheight, frame.nYmbwidth, frame.nYmbheight, nImFps, nSARw, nSARh);
            stream.Flush(); // Flush data to the List<byte>
            sps = baseStream.ToArray();
            baseStream.SetLength(0);

            CreatePPS();
            stream.Flush(); // Flush data to the List<byte>
            pps = baseStream.ToArray();
            baseStream.SetLength(0);
        }

        //! It codes the frame that is in frame memory a it saves the coded data to disc
        //Codifies & save the video frame (it only uses 16x16 intra PCM -> NO COMPRESSION!)
        public void CodeAndSaveFrame(Span<byte> frameBuffer)
        {
            baseStream.SetLength(0);

            //The slice header is not byte aligned, so the first macroblock header is not byte aligned
            CreateSliceHeader(m_lNumFramesAdded);

            //Loop over macroblock size
            for (int y = 0; y < frame.nYheight / frame.nYmbheight; y++)
            {
                for (int x = 0; x < frame.nYwidth / frame.nYmbwidth; x++)
                {
                    CreateMacroblock(y, x, frameBuffer);
                }
            }

            CreateSliceFooter();
            stream.DoByteAlign();

            m_lNumFramesAdded++;

            // flush
            stream.Flush();
            nal = baseStream.ToArray();
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
            stream.Flush();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    stream.Dispose();
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