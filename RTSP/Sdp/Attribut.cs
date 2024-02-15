using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Rtsp.Sdp
{
    public class Attribut
    {
        private static readonly Dictionary<string, Type> attributMap = new(StringComparer.Ordinal)
        {
            {AttributRtpMap.NAME,typeof(AttributRtpMap)},
            {AttributFmtp.NAME,typeof(AttributFmtp)},
        };

        public virtual string Key { get; }
        public virtual string Value { get; protected set; } = string.Empty;

        public static void RegisterNewAttributeType(string key, Type attributType)
        {
            if (!attributType.IsSubclassOf(typeof(Attribut)))
                throw new ArgumentException("Type must be subclass of Rtsp.Sdp.Attribut", nameof(attributType));

            attributMap[key] = attributType;
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

            Attribut returnValue;

            // Call parser of child type
            if (attributMap.TryGetValue(listValues[0], out var childType))
            {
                var defaultContructor = childType.GetConstructor(Type.EmptyTypes);
                Debug.Assert(defaultContructor is not null, "The child type must have an empty constructor");
                returnValue = (defaultContructor!.Invoke(Type.EmptyTypes) as Attribut)!;
            }
            else
            {
                returnValue = new Attribut(listValues[0]);
            }
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
