namespace Rtsp.Rtp
{
    public interface IPayloadProcessor
    {

        /// <summary>
        /// Process an RtpPacket and return a RawMediaFrame containing the data of the stream.
        /// </summary>
        /// <remarks>return value should be disposed after copying the data to allow buffer to be reuse</remarks>
        /// <param name="packet">packet to handle</param>
        /// <returns>RawMedia frame containing the stream data or empty if more packet are needed</returns>
        RawMediaFrame ProcessPacket(RtpPacket packet);
    }
}
