using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightDownloader.Core.Models;

namespace LightDownloader.App;

/// <summary>The icons Windows Explorer shows for a file type or a folder (cached per extension).</summary>
internal static class ShellIcons
{
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
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private const uint Icon = 0x100, SmallIcon = 0x1, UseFileAttributes = 0x10, TypeNameFlag = 0x400;
    private const uint NormalFile = 0x80, Directory = 0x10;

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForFile(string? fileName, bool large = false)
    {
        var ext = Path.GetExtension(fileName ?? "");
        if (ext.Length < 2) ext = ".file";
        return Get("x" + ext, NormalFile, large);
    }

    public static ImageSource? Folder(bool large = false) => Get("folder", Directory, large);

    /// <summary>Explorer's type name, e.g. "Compressed (zipped) Folder".</summary>
    public static string TypeName(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        if (ext.Length < 2) return "File";
        var info = new ShFileInfo();
        SHGetFileInfo("x" + ext, NormalFile, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), TypeNameFlag | UseFileAttributes);
        return string.IsNullOrWhiteSpace(info.szTypeName) ? ext.TrimStart('.').ToUpperInvariant() + " File" : info.szTypeName;
    }

    private static ImageSource? Get(string path, uint attributes, bool large)
    {
        var key = (attributes == Directory ? "<dir>" : path) + (large ? "|L" : "|S");
        if (Cache.TryGetValue(key, out var cached)) return cached;

        ImageSource? image = null;
        var info = new ShFileInfo();
        var flags = Icon | UseFileAttributes | (large ? 0 : SmallIcon);
        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                image = bmp;
            }
            catch { /* leave it blank */ }
            finally { DestroyIcon(info.hIcon); }
        }
        Cache[key] = image;
        return image;
    }
}

/// <summary>Small shell icon for a row: [0] file name, [1] <see cref="DownloadKind"/> (videos without a name yet show as MP4).</summary>
public sealed class FileIconConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var name = values.Length > 0 ? values[0] as string : null;
        if (values.Length > 1 && values[1] is DownloadKind.Video && !Path.HasExtension(name)) name = "video.mp4";
        return ShellIcons.ForFile(name, large: parameter is "large");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
