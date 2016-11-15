using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

// (c) Roger Hardiman 2016

// This class uses a System Timer to generate a YUV image at regular intervals
// The ReceivedYUVFrame event is fired for each new YUV image

public class TestCard
{

    // Events that applications can receive
    public event ReceivedYUVFrameHandler ReceivedYUVFrame;

    // Delegated functions (essentially the function prototype)
    public delegate void ReceivedYUVFrameHandler(uint timestamp, int width, int height, byte[] data);


    // Local variables
    private System.Timers.Timer frame_timer;
    private Stopwatch stopwatch;
    private byte[] yuv_frame = null;
    private int x_position = 0;
    private int y_position = 0;
    private int width = 0;
    private int height = 0;

    // Constructor
    public TestCard(int width, int height, int fps)
    {
        this.width = width;
        this.height = height;

        // YUV size
        int y_size = width * height;
        int u_size = (width >> 1) * (height >> 1);
        int v_size = (width >> 1) * (height >> 1);
        yuv_frame = new byte[y_size + u_size + v_size];

        // Set all values to 127
        for (int x = 0; x < yuv_frame.Length; x++)
        {
            yuv_frame[x] = 127;
        }

        stopwatch = new Stopwatch();
        stopwatch.Start();

        // Start timer. The Timer will generate a YUV frame on every 'tick'
        int interval_ms = 1000 / fps;
        frame_timer = new System.Timers.Timer();
        frame_timer.Interval = interval_ms;
        frame_timer.AutoReset = true; // restart timer after the time has elapsed
        frame_timer.Elapsed += (object sender, System.Timers.ElapsedEventArgs e) =>
        {
            // send a keepalive message
            Send_YUV_Frame();
        };
        frame_timer.Start();
        
    }

    // Dispose
    public void Disconnect()
    {
        // Stop the frame timer
        frame_timer.Stop();
        frame_timer.Dispose();
    }


    private void Send_YUV_Frame()
    {

        // Toggle the pixel value
        byte pixel_value = yuv_frame[(y_position * width) + x_position];

        // change brightness of pixel			
        if (pixel_value > 128) pixel_value = 30;
        else pixel_value = 230;

        yuv_frame[(y_position * width) + x_position] = pixel_value;

        // move the x and y position
        x_position = x_position + 5;
        if (x_position >= width)
        {
            x_position = 0;
            y_position = y_position + 1;
        }

        if (y_position >= height)
        {
            y_position = 0;
        }

        // fire the Event
        if (ReceivedYUVFrame != null)
        {
            ReceivedYUVFrame((uint)stopwatch.ElapsedMilliseconds, width, height, yuv_frame);
        }
    }

}
