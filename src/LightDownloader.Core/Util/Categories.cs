namespace LightDownloader.Core.Util;

/// <summary>IDM-style file categories, detected from the file extension.</summary>
public static class Categories
{
    public const string Compressed = "Compressed";
    public const string Documents = "Documents";
    public const string Music = "Music";
    public const string Video = "Video";
    public const string Programs = "Programs";
    public const string Pictures = "Pictures";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All =
        new[] { Compressed, Documents, Music, Video, Programs, Pictures, Other };

    private static readonly Dictionary<string, string> ByExtension = Build();

    public static string FromFileName(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return ext.Length > 0 && ByExtension.TryGetValue(ext, out var c) ? c : Other;
    }

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string category, string extensions)
        {
            foreach (var e in extensions.Split(' ')) map["." + e] = category;
        }
        Add(Compressed, "zip rar 7z tar gz tgz bz2 xz zst cab arj lzh ace");
        Add(Documents, "pdf doc docx xls xlsx ppt pptx odt ods odp rtf txt csv md epub mobi");
        Add(Music, "mp3 flac wav aac m4a ogg oga opus wma aiff alac mid midi");
        Add(Video, "mp4 mkv avi mov webm flv wmv m4v ts m2ts 3gp mpeg mpg vob");
        Add(Programs, "exe msi msix msixbundle appx appxbundle apk dmg pkg deb rpm iso img bat cmd jar");
        Add(Pictures, "jpg jpeg png gif bmp webp svg tif tiff ico heic avif psd");
        return map;
    }
}
