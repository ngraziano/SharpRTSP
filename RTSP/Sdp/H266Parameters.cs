using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Parse 'fmtp' attribute in SDP
// Extract H266 fields

namespace Rtsp.Sdp
{
    public class H266Parameters : IDictionary<string, string>
    {
        private readonly Dictionary<string, string> parameters = [];

        public IList<byte[]> SpropParameterSets
        {
            get
            {
                List<byte[]> result = [];

                if (ContainsKey("sprop-dci") && this["sprop-dci"] != null)
                {
                    result.AddRange(this["sprop-dci"].Split(',').Select(x => Convert.FromBase64String(x)));
                }
                else
                {
                    result.Add(Array.Empty<byte>());
                }

                if (ContainsKey("sprop-vps") && this["sprop-vps"] != null)
                {
                    result.AddRange(this["sprop-vps"].Split(',').Select(x => Convert.FromBase64String(x)));
                }
                else
                {
                    result.Add(Array.Empty<byte>());
                }

                if (ContainsKey("sprop-sps") && this["sprop-sps"] != null)
                {
                    result.AddRange(this["sprop-sps"].Split(',').Select(x => Convert.FromBase64String(x)));
                }
                else
                {
                    result.Add(Array.Empty<byte>());
                }

                if (ContainsKey("sprop-pps") && this["sprop-pps"] != null)
                {
                    result.AddRange(this["sprop-pps"].Split(',').Select(x => Convert.FromBase64String(x)));
                }
                else
                {
                    result.Add(Array.Empty<byte>());
                }

                if (ContainsKey("sprop-sei") && this["sprop-sei"] != null)
                {
                    result.AddRange(this["sprop-sei"].Split(',').Select(x => Convert.FromBase64String(x)));
                }
                else
                {
                    result.Add(Array.Empty<byte>());
                }

                return result;
            }
        }

        public static H266Parameters Parse(string parameterString)
        {
            var result = new H266Parameters();
            foreach (var pair in parameterString.Split(';').Select(x => x.Trim().Split('=', 2)))
            {
                if (!string.IsNullOrWhiteSpace(pair[0]))
                    result[pair[0]] = pair.Length > 1 ? pair[1] : string.Empty;
            }

            return result;
        }

        public override string ToString() =>
            parameters.Select(p => p.Key + (p.Value != null ? "=" + p.Value : string.Empty))
                .Aggregate((x, y) => x + ";" + y);

        public string this[string index]
        {
            get => parameters[index];
            set => parameters[index] = value;
        }

        public int Count => parameters.Count;

        public bool IsReadOnly => ((IDictionary<string, string>)parameters).IsReadOnly;

        public ICollection<string> Keys => ((IDictionary<string, string>)parameters).Keys;

        public ICollection<string> Values => ((IDictionary<string, string>)parameters).Values;

        public void Add(KeyValuePair<string, string> item) => ((IDictionary<string, string>)parameters).Add(item);

        public void Add(string key, string value) => parameters.Add(key, value);

        public void Clear() => parameters.Clear();

        public bool Contains(KeyValuePair<string, string> item) =>
            ((IDictionary<string, string>)parameters).Contains(item);

        public bool ContainsKey(string key) => parameters.ContainsKey(key);

        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) =>
            ((IDictionary<string, string>)parameters).CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            ((IDictionary<string, string>)parameters).GetEnumerator();

        public bool Remove(KeyValuePair<string, string> item) => ((IDictionary<string, string>)parameters).Remove(item);

        public bool Remove(string key) => parameters.Remove(key);

        public bool TryGetValue(string key, out string value) => parameters.TryGetValue(key, out value!);

        IEnumerator IEnumerable.GetEnumerator() => ((IDictionary<string, string>)parameters).GetEnumerator();
    }
}