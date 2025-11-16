namespace Rtsp.Utils;

using Rtsp.Messages;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

internal class SentMessageList
{
    private readonly Dictionary<int, RtspRequest> _sentMessage = [];
    private uint _nbAddSinceLastCleanup;

    public void Add(int cSeq, RtspRequest originalMessage)
    {
        lock (_sentMessage)
        {
            _nbAddSinceLastCleanup++;
            if (_sentMessage.Count > 10 && _nbAddSinceLastCleanup > 100)
            {
                //cleanup
                foreach (var key in _sentMessage.Keys.Where(k => k < cSeq - 100).ToArray())
                {
                    _sentMessage.Remove(key);
                }
                _nbAddSinceLastCleanup = 0;
            }

            _sentMessage[cSeq] = originalMessage;
        }
    }

    public bool TryPopValue(int cSeq, [MaybeNullWhen(false)] out RtspRequest? originalRequest)
    {
        lock (_sentMessage)
        {
            if (_sentMessage.TryGetValue(cSeq, out originalRequest))
            {
                _sentMessage.Remove(cSeq);
                return true;
            }
            return false;
        }
    }
}
