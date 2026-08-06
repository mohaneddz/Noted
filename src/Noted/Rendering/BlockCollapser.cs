using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Noted.Rendering;

/// <summary>
/// A generator that, as well as producing an inline visual for a multi-line block, can say which
/// line ranges it is currently collapsing. AvalonEdit refuses to let an element span lines that
/// aren't explicitly collapsed, so <see cref="BlockCollapser"/> uses this to hide a block's
/// interior lines while the block is shown as a single rendered object.
/// </summary>
public interface ICollapsibleBlockSource
{
    /// <summary>Inclusive line ranges the source is rendering as one collapsed visual right now.</summary>
    IEnumerable<(int Start, int End)> CollapsedBlockRanges(TextDocument document);
}

/// <summary>
/// Keeps AvalonEdit's collapsed-line state in step with the block generators. Whenever the caret
/// moves, the document changes, or live-markdown is toggled, it recomputes which block interiors
/// should be hidden and folds/unfolds them — the same primitive AvalonEdit's own folding is built on.
/// </summary>
public sealed class BlockCollapser
{
    private readonly TextView _textView;
    private readonly IReadOnlyList<ICollapsibleBlockSource> _sources;
    private readonly List<CollapsedLineSection> _sections = new();
    private List<(int Start, int End)> _current = new();
    private bool _updating;

    public BlockCollapser(TextView textView, IReadOnlyList<ICollapsibleBlockSource> sources)
    {
        _textView = textView;
        _sources = sources;
    }

    /// <summary>Re-fold every block interior that should currently be hidden. Cheap to call on every
    /// caret move: it only touches the height tree when the set of collapsed ranges actually changes.</summary>
    public void Update()
    {
        if (_updating) return;

        var document = _textView.Document;
        if (document is null) return;

        var desired = new List<(int Start, int End)>();
        foreach (var source in _sources)
        {
            foreach (var (start, end) in source.CollapsedBlockRanges(document))
            {
                if (end > start && start >= 1 && end <= document.LineCount) desired.Add((start, end));
            }
        }

        if (desired.Count == _current.Count && desired.SequenceEqual(_current)) return;

        _updating = true;
        try
        {
            foreach (var section in _sections)
            {
                try { section.Uncollapse(); }
                catch (InvalidOperationException) { /* already gone after an edit — ignore */ }
            }
            _sections.Clear();

            foreach (var (start, end) in desired)
            {
                // Keep the block's first line visible to host the rendered element; hide the rest.
                var first = document.GetLineByNumber(start + 1);
                var last = document.GetLineByNumber(end);
                _sections.Add(_textView.CollapseLines(first, last));
            }

            _current = desired;
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Drop all folds (e.g. when live markdown is switched off or the document is replaced).</summary>
    public void Clear()
    {
        foreach (var section in _sections)
        {
            try { section.Uncollapse(); }
            catch (InvalidOperationException) { }
        }
        _sections.Clear();
        _current = new();
    }
}
