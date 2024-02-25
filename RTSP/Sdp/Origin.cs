using System;

namespace Rtsp.Sdp
{
    /// <summary>
    /// Object ot represent orgin in an Session Description Protocol
    /// </summary>
    public class Origin
    {
        /// <summary>
        /// Parses the specified origin string.
        /// </summary>
        /// <param name="originString">The string to convert to origin object.</param>
        /// <returns>The parsed origin object</returns>
        public static Origin Parse(string originString)
        {
            if (originString == null)
                throw new ArgumentNullException(nameof(originString));

            string[] parts = originString.Split(' ');

            if (parts.Length != 6)
                throw new FormatException("Number of element invalid in origin string.");

            return new()
            {
                Username = parts[0],
                SessionId = parts[1],
                SessionVersion = parts[2],
                NetType = parts[3],
                AddressType = parts[4],
                UnicastAddress = parts[5],
            };
        }

        /// <summary>
        /// Gets or sets the username.
        /// </summary>
        /// <remarks>It is the user's login on the originating host, or it is "-"
        /// if the originating host does not support the concept of user IDs.
        /// This MUST NOT contain spaces</remarks>
        /// <value>The username.</value>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the session id.
        /// </summary>
        /// <remarks>It is a numeric string such that the tuple of <see cref="Username"/>,
        /// <see cref="SessionId"/>, <see cref="NetType"/>, <see cref="AddressType"/>, and <see cref="UnicastAddress"/> forms a
        /// globally unique identifier for the session.  The method of
        /// <see cref="SessionId"/> allocation is up to the creating tool, but it has been
        /// suggested that a Network Time Protocol (NTP) format timestamp be
        /// used to ensure uniqueness</remarks>
        /// <value>The session id.</value>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the session version.
        /// </summary>
        /// <value>The session version.</value>
        public string SessionVersion { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of the net.
        /// </summary>
        /// <value>The type of the net.</value>
        public string NetType { get; set; } = string.Empty;

        /// <see cref="SessionId"/><summary>
        /// Gets or sets the type of the address.
        /// </summary>
        /// <value>The type of the address.</value>
        public string AddressType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the unicast address (IP or FDQN).
        /// </summary>
        /// <value>The unicast address.</value>
        public string UnicastAddress { get; set; } = string.Empty;

        public override string ToString()
        {
            return string.Join(" ",
                [
                    Username,
                    SessionId,
                    SessionVersion,
                    NetType,
                    AddressType,
                    UnicastAddress,
                ]
                );
        }
    }
}
