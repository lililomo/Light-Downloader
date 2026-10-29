using System.Text.Json.Serialization;

namespace LightDownloader.Core.Models;

/// <summary>
/// One byte range of a download. <see cref="Start"/> and <see cref="End"/> are
/// inclusive HTTP range offsets. <see cref="Downloaded"/> is a public field so the
/// engine can update it with <see cref="System.Threading.Interlocked"/>.
/// </summary>
public sealed class SegmentState
{
    public long Start;
    public long End;        // inclusive; -1 when the total size is unknown
    public long Downloaded;

    [JsonIgnore] public long Length => End < 0 ? -1 : (End - Start + 1);
    [JsonIgnore] public bool IsComplete => End >= 0 && Downloaded >= Length;

    public SegmentState() { }

    public SegmentState(long start, long end)
    {
        Start = start;
        End = end;
    }
}
