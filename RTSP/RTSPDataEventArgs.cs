using Rtsp.Messages;
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
        /// <param name="data">Data .</param>
        public RtspDataEventArgs(RtspData data)
        {
            Data = data;
        }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public RtspData Data { get; set; }
    }
}
