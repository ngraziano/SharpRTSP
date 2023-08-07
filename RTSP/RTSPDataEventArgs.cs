using System;

namespace Rtsp
{
    /// <summary>
    /// Event args containing information for message events.
    /// </summary>
    public class RtspDataEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPDataEventArgs"/> class.
        /// </summary>
        /// <param name="data">A data.</param>
        public RtspDataEventArgs(byte[] data)
        {
            Data = data;
        }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public byte[] Data { get; set; }
    }
}
