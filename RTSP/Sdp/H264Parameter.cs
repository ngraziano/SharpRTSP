using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Sdp
{
    public class H264Parameters : IDictionary<string, string>
    {
        private readonly Dictionary<string, string> parameters = new ();

        public List<byte[]> SpropParameterSets
        {
            get
            {
                List<byte[]> result = new();

                if (ContainsKey("sprop-parameter-sets") && this["sprop-parameter-sets"] != null)
                {
                    result.AddRange(this["sprop-parameter-sets"].Split(',').Select(x => Convert.FromBase64String(x)));
                }

                return result;
            }
        }

        public static H264Parameters Parse(string parameterString)
        {
            var result = new H264Parameters();
            foreach (var pair in parameterString.Split(';').Select(x => x.Trim().Split(new char[] { '=' }, 2)))
            {
                if (!string.IsNullOrWhiteSpace(pair[0]))
                    result[pair[0]] = pair.Length > 1 ? pair[1] : string.Empty;
            }
            return result;
        }

        public override string ToString()
        {
            return parameters.Select(p => p.Key + (p.Value != null ? "=" + p.Value : string.Empty)).Aggregate((x, y) => x + ";" + y);
        }

        public string this[string index]
        {
            get { return parameters[index]; }
            set { parameters[index] = value; }
        }

        public int Count
        {
            get
            {
                return parameters.Count;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return ((IDictionary<string, string>)parameters).IsReadOnly;
            }
        }

        public ICollection<string> Keys
        {
            get
            {
                return ((IDictionary<string, string>)parameters).Keys;
            }
        }

        public ICollection<string> Values
        {
            get
            {
                return ((IDictionary<string, string>)parameters).Values;
            }
        }

        public void Add(KeyValuePair<string, string> item)
        {
            ((IDictionary<string, string>)parameters).Add(item);
        }

        public void Add(string key, string value)
        {
            parameters.Add(key, value);
        }

        public void Clear()
        {
            parameters.Clear();
        }

        public bool Contains(KeyValuePair<string, string> item)
        {
            return ((IDictionary<string, string>)parameters).Contains(item);
        }

        public bool ContainsKey(string key)
        {
            return parameters.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            ((IDictionary<string, string>)parameters).CopyTo(array, arrayIndex);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return ((IDictionary<string, string>)parameters).GetEnumerator();
        }

        public bool Remove(KeyValuePair<string, string> item)
        {
            return ((IDictionary<string, string>)parameters).Remove(item);
        }

        public bool Remove(string key)
        {
            return parameters.Remove(key);
        }

        public bool TryGetValue(string key, out string value)
        {
            return parameters.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IDictionary<string, string>)parameters).GetEnumerator();
        }
    }
}
