using System.ComponentModel;
using System.Windows.Media;

namespace LightDownloader.App;

/// <summary>One sidebar entry: a status filter, a file category, or a section header.</summary>
public sealed class NavEntry : INotifyPropertyChanged
{
    public const string AllKey = "all";
    public const string ActiveKey = "active";
    public const string UnfinishedKey = "unfinished";
    public const string FinishedKey = "finished";

    public NavEntry(string key, string label, string glyph, Brush brush)
    {
        Key = key;
        Label = label;
        Glyph = glyph;
        Brush = brush;
    }

    private NavEntry(string label)
    {
        Key = "";
        Label = label;
        Glyph = "";
        Brush = Brushes.Transparent;
        IsHeader = true;
    }

    public static NavEntry Header(string label) => new(label);

    public string Key { get; }
    public string Label { get; }
    public string Glyph { get; }
    public Brush Brush { get; }
    public bool IsHeader { get; }

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
