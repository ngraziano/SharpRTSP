namespace Rtsp
{
    public interface IRtspListenSocket
    {
        IRtspTransport Accept();
        void Start();
        void Stop();
    }
}