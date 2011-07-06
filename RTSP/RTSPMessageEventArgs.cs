using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RTSP
{
    using Messages;
    /// <summary>
    /// Event args containing information for message events.
    /// </summary>
    public class RTSPChunkEventArgs :EventArgs
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="RTSPChunkEventArgs"/> class.
        /// </summary>
        /// <param name="aMessage">A message.</param>
        public RTSPChunkEventArgs(RTSPChunk aMessage)
        {
            Message = aMessage;
        }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public RTSPChunk Message { get; set; }
    }
}
