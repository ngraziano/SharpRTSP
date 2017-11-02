public class RtspPushSession
{
    public string Sdp { get; }
    public string AbsolutePath { get; }

    public RtspPushSession(string absolutePath, string sdp)
    {
        AbsolutePath = absolutePath;
        Sdp = sdp;
    }

}