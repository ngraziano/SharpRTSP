using System.Text;

namespace Rtsp.Utils;

internal static class StringBuilderExtensions
{
    extension(StringBuilder builder)
    {
        public StringBuilder AppendHttpLine()
        {
            return builder.Append("\r\n");
        }
        
        public StringBuilder AppendHttpLine(string value)
        {
            return builder.Append(value).Append("\r\n");
        }
    }
    
}