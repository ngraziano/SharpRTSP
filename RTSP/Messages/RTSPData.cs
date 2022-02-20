using System.Text;

namespace Rtsp.Messages
{
    /// <summary>
    /// Message wich represent data. ($ limited message)
    /// </summary>
    public class RtspData : RtspChunk
    {
        /// <summary>
        /// Create a string of the message for debug.
        /// </summary>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("Data message");
            if (Data == null)
                stringBuilder.AppendLine("Data : null");
            else
                stringBuilder.AppendLine($"Data length :-{Data.Length}-");

            return stringBuilder.ToString();
        }

        public int Channel { get; set; }

        /// <summary>
        /// Clones this instance.
        /// <remarks>Listner is not cloned</remarks>
        /// </summary>
        /// <returns>a clone of this instance</returns>
        public override object Clone()
        {
            RtspData result = new RtspData();
            result.Channel = this.Channel;
            if (this.Data != null)
                result.Data = this.Data.Clone() as byte[];
            result.SourcePort = this.SourcePort;
            return result;
        }
    }
}
