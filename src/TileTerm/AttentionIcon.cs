using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TileTerm;

/// <summary>
/// The app icon with a red dot at its top-right corner, which <see cref="MainWindow.ShowAttention"/> sets as
/// the window icon (taskbar button, Alt+Tab) while a tile's notification is unseen.
///
/// Not a taskbar overlay (<c>TaskbarItemInfo.Overlay</c>): Windows always puts the overlay at the icon's
/// lower right, and the dot belongs at the upper right. Built as a multi-size icon — every size of
/// <c>Assets/TileTerm.ico</c> with the dot drawn on it — so Windows picks a sharp image for each place
/// instead of scaling one bitmap.
/// </summary>
internal static class AttentionIcon
{
    private static readonly Color DotColor = Color.FromRgb(0xE8, 0x11, 0x23);

    /// <summary>Dot diameter as a fraction of the icon size: 9px on the taskbar's 24px icon.</summary>
    private const double DotFraction = 9.0 / 24.0;

    private static BitmapFrame? s_icon;

    public static BitmapFrame Get() => s_icon ??= Create();

    private static BitmapFrame Create()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/TileTerm.ico"))
            ?? throw new InvalidOperationException("Assets/TileTerm.ico is missing");
        BitmapDecoder decoder;
        using (resource.Stream)
            decoder = new IconBitmapDecoder(resource.Stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var images = new List<(int Size, byte[] Png)>();
        foreach (var frame in decoder.Frames)
        {
            int size = frame.PixelWidth;
            if (size > 256) continue;   // not representable in an .ico directory entry
            images.Add((size, EncodePng(WithDot(frame, size))));
        }

        // Window.Icon looks through the frame's Decoder for the best size for each use, so hand it a frame
        // straight from the decoder (BitmapFrame.Create(...) would drop the link).
        var ico = new MemoryStream(BuildIco(images));
        return new IconBitmapDecoder(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static BitmapSource WithDot(BitmapSource frame, int size)
    {
        double d = Math.Max(5, Math.Round(size * DotFraction));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(frame, new Rect(0, 0, size, size));
            dc.DrawEllipse(new SolidColorBrush(DotColor), null, new Point(size - d / 2, d / 2), d / 2, d / 2);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>An .ico file whose images are PNG-compressed (supported since Windows Vista); WPF has no
    /// icon encoder.</summary>
    private static byte[] BuildIco(List<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type: icon
        w.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size == 256 ? 0 : size));   // width; 0 means 256
            w.Write((byte)(size == 256 ? 0 : size));   // height
            w.Write((byte)0);            // palette size
            w.Write((byte)0);            // reserved
            w.Write((ushort)1);          // color planes
            w.Write((ushort)32);         // bits per pixel
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images)
            w.Write(png);
        w.Flush();
        return stream.ToArray();
    }
}
