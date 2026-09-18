using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// Turns a profile into the icon shown for it (settings list, tile title bar, favorites
/// bar). Rules: a picked image file is shown as-is; any other picked file contributes its
/// own Windows shell icon; with nothing picked, the icon of the profile's executable is
/// used. If none of that yields an image (no executable yet, missing file, ...) a small
/// tile with the name's first letter stands in, so callers always get something to draw.
/// </summary>
internal static class ProfileIcons
{
    private const int LetterTileSize = 32;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff",
    };

    private static readonly Dictionary<(string Path, long Stamp), ImageSource?> Cache = new();
    private static readonly Dictionary<char, ImageSource> LetterCache = new();

    /// <summary>File-dialog filter for picking an icon: images first, but any file is allowed
    /// (its shell icon is used).</summary>
    public const string PickerFilter =
        "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff|すべてのファイル (*.*)|*.*";

    public static ImageSource Get(ProfileDefinition profile) => Get(profile.IconPath, profile.Executable, profile.Name);

    public static ImageSource Get(string? iconPath, string? executable, string? name)
    {
        var fromIconPath = LoadFile(iconPath);
        if (fromIconPath is not null) return fromIconPath;

        var fromExecutable = LoadFile(ResolveExecutable(executable));
        if (fromExecutable is not null) return fromExecutable;

        return LetterTile(name);
    }

    /// <summary>A ready-to-place <see cref="Image"/> for <paramref name="source"/> at the given size.</summary>
    public static Image CreateImage(ImageSource source, double size = 16)
    {
        var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static ImageSource? LoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (!File.Exists(path)) return null;

            var key = (path, File.GetLastWriteTimeUtc(path).Ticks);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            ImageSource? loaded = null;
            if (ImageExtensions.Contains(Path.GetExtension(path)))
                loaded = LoadImage(path);
            loaded ??= LoadShellIcon(path);

            Cache[key] = loaded;
            return loaded;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            // Decode through the decoder rather than BitmapImage so a multi-size .ico yields
            // its largest frame, then shrink big photos to something icon-sized.
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();

            BitmapSource result = frame;
            const double maxSide = 128;
            double scale = Math.Min(1.0, maxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (scale < 1.0)
                result = new TransformedBitmap(frame, new ScaleTransform(scale, scale));

            result.Freeze();
            return result;
        }
        catch
        {
            return null; // not actually a decodable image — caller falls back to the shell icon
        }
    }

    private static ImageSource? LoadShellIcon(string path)
    {
        var info = new ShFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon) == IntPtr.Zero ||
            info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>A bare command like "pwsh" or "wsl.exe" isn't a path the shell can give an
    /// icon for; find it the way launching it would.</summary>
    private static string? ResolveExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        if (Path.IsPathRooted(executable)) return executable;

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), executable);
                if (File.Exists(candidate)) return candidate;
                foreach (var ext in extensions)
                    if (File.Exists(candidate + ext)) return candidate + ext;
            }
            catch
            {
                // malformed PATH entry — skip it
            }
        }
        return null;
    }

    private static ImageSource LetterTile(string? name)
    {
        char letter = string.IsNullOrWhiteSpace(name) ? '?' : char.ToUpperInvariant(name.Trim()[0]);
        if (LetterCache.TryGetValue(letter, out var cached)) return cached;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x4A, 0x4D, 0x52)), null,
                new Rect(0, 0, LetterTileSize, LetterTileSize), 5, 5);
            var text = new FormattedText(
                letter.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), LetterTileSize * 0.6, Brushes.Gainsboro, 1.0);
            dc.DrawText(text, new Point((LetterTileSize - text.Width) / 2, (LetterTileSize - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(LetterTileSize, LetterTileSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        LetterCache[letter] = bitmap;
        return bitmap;
    }
}
