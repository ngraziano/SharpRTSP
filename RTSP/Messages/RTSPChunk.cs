using System;

namespace Rtsp.Messages
{
    /// <summary>
    /// Class wich represent each message echanged on Rtsp socket.
    /// </summary>
    public abstract class RtspChunk : ICloneable
    {
        /// <summary>
        /// Gets or sets the data associate with the message.
        /// </summary>
        /// <value>Array of byte transmit with the message.</value>
        public virtual Memory<byte> Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets or sets the source port wich receive the message.
        /// </summary>
        /// <value>The source port.</value>
        public RtspListener? SourcePort { get; set; }

        #region ICloneable Membres

        /// <summary>
        /// Crée un nouvel objet qui est une copie de l'instance en cours.
        /// </summary>
        /// <returns>
        /// Nouvel objet qui est une copie de cette instance.
        /// </returns>
        public abstract object Clone();

        #endregion
    }
}
