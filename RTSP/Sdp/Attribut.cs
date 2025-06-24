using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

namespace Rtsp.Sdp
{
    public class Attribut
    {
        private static readonly Dictionary<string, Func<Attribut>> attributMap = new(StringComparer.Ordinal)
        {
            {AttributRtpMap.NAME, () => new AttributRtpMap() },
            {AttributFmtp.NAME  , () => new AttributFmtp()   },
        };

        public virtual string Key { get; }
        public virtual string Value { get; protected set; } = string.Empty;

        public static void RegisterNewAttributeType(string key, Func<Attribut> attributTypeConstructor)
        {
            attributMap[key] = attributTypeConstructor;
        }

        public Attribut(string key)
        {
            Key = key;
        }

        public static Attribut ParseInvariant(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Contract.EndContractBlock();

            var listValues = value.Split(':', 2);

            // Call parser of child type
            if (!attributMap.TryGetValue(listValues[0], out var childContructor))
            {
                childContructor = () => new Attribut(listValues[0]);
            }

            var returnValue = childContructor.Invoke();
            // Parse the value. Note most attributes have a value but recvonly does not have a value
            if (listValues.Length > 1) returnValue.ParseValue(listValues[1]);

            return returnValue;
        }

        protected virtual void ParseValue(string value)
        {
            Value = value;
        }
    }
}
