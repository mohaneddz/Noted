using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using ICSharpCode.AvalonEdit.Document;

namespace Noted.Models;

/// <summary>One open tab: its text, where it came from, and whether it still matches disk.</summary>
public sealed class NoteDocument : INotifyPropertyChanged
{
    private static int _untitledCounter;

    private string? _filePath;
    private bool _isModified;

    private NoteDocument(string text, string? filePath, Encoding encoding)
    {
        Document = new TextDocument(text) { FileName = filePath };
        Document.UndoStack.SizeLimit = 500;
        Document.TextChanged += (_, _) => IsModified = true;
        _filePath = filePath;
        Encoding = encoding;
        UntitledNumber = filePath is null ? ++_untitledCounter : 0;
    }

    public TextDocument Document { get; }

    public Encoding Encoding { get; set; }

    public int UntitledNumber { get; }

    public int CaretOffset { get; set; }

    public double ScrollOffset { get; set; }

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (_filePath == value) return;
            _filePath = value;
            Document.FileName = value;
            Raise(nameof(FilePath));
            Raise(nameof(Title));
            Raise(nameof(ToolTip));
        }
    }

    public bool IsModified
    {
        get => _isModified;
        private set
        {
            if (_isModified == value) return;
            _isModified = value;
            Raise(nameof(IsModified));
            Raise(nameof(Title));
        }
    }

    public string Title => FilePath is null
        ? $"Untitled {UntitledNumber}"
        : Path.GetFileName(FilePath);

    public string ToolTip => FilePath ?? Title;

    public static NoteDocument CreateEmpty() => new(string.Empty, null, new UTF8Encoding(false));

    public static NoteDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(bytes, out int preambleLength);
        string text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);

        var note = new NoteDocument(text, Path.GetFullPath(path), encoding);
        note.Document.UndoStack.ClearAll();
        note.IsModified = false;
        return note;
    }

    public void Save(string path)
    {
        path = Path.GetFullPath(path);
        var preamble = Encoding.GetPreamble();
        var body = Encoding.GetBytes(Document.Text);

        var buffer = new byte[preamble.Length + body.Length];
        preamble.CopyTo(buffer, 0);
        body.CopyTo(buffer, preamble.Length);

        File.WriteAllBytes(path, buffer);

        FilePath = path;
        IsModified = false;
    }

    public void MarkClean() => IsModified = false;

    private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            preambleLength = 3;
            return new UTF8Encoding(true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            preambleLength = 2;
            return new UnicodeEncoding(false, true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            preambleLength = 2;
            return new UnicodeEncoding(true, true);
        }

        preambleLength = 0;
        return new UTF8Encoding(false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
