namespace Rtsp.Messages
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Globalization;

    /// <summary>
    /// Describe a couple of port used to transfer video and command.
    /// </summary>
    public class PortCouple
    {
        /// <summary>
        /// Gets or sets the first port number.
        /// </summary>
        /// <value>The first port.</value>
        public int First { get; set; }
        /// <summary>
        /// Gets or sets the second port number.
        /// </summary>
        /// <remarks>If not present the value is 0</remarks>
        /// <value>The second port.</value>
        public int Second { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PortCouple"/> class.
        /// </summary>
        public PortCouple()
        { }
        /// <summary>
        /// Initializes a new instance of the <see cref="PortCouple"/> class.
        /// </summary>
        /// <param name="first">The first port.</param>
        public PortCouple(int first)
        {
            First = first;
            Second = 0;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="PortCouple"/> class.
        /// </summary>
        /// <param name="first">The first port.</param>
        /// <param name="second">The second port.</param>
        public PortCouple(int first, int second)
        {
            First = first;
            Second = second;
        }

        /// <summary>
        /// Gets a value indicating whether this instance has second port.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance has second port; otherwise, <c>false</c>.
        /// </value>
        public bool IsSecondPortPresent
        {
            get { return Second != 0; }
        }

        /// <summary>
        /// Parses the int values of port.
        /// </summary>
        /// <param name="stringValue">A string value.</param>
        /// <returns>The port couple</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stringValue"/> is <c>null</c>.</exception>
        public static PortCouple Parse(string stringValue)
        {
            if (stringValue == null)
                throw new ArgumentNullException(nameof(stringValue));
            Contract.Requires(!string.IsNullOrEmpty(stringValue));

            string[] values = stringValue.Split('-');

            _ = int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tempValue);
            PortCouple result = new(tempValue);

            tempValue = 0;
            if (values.Length > 1)
                _ = int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out tempValue);

            result.Second = tempValue;

            return result;
        }

        /// <summary>
        /// Returns a <see cref="string"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return IsSecondPortPresent ? FormattableString.Invariant($"{First}-{Second}") : First.ToString(CultureInfo.InvariantCulture);
        }
    }
}
