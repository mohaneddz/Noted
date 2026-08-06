namespace Noted.Markdown;

/// <summary>
/// Single-pass, allocation-light markdown tokenizer that works one line at a time.
///
/// It is deliberately *not* a CommonMark parser: it only needs to know where syntax
/// characters live so the editor can dim or hide them, which means it can stay
/// line-local and therefore cheap enough to run on every repaint.
/// </summary>
public static class MarkdownScanner
{
    public static MdLine ScanFenceDelimiter(string line)
    {
        return new MdLine
        {
            Block = MdStyle.CodeBlock,
            Tokens = [new MdToken(0, line.Length, MdStyle.Marker | MdStyle.CodeBlock)],
            AllMarkers = true,
        };
    }

    public static MdLine ScanFencedContent(string line)
    {
        if (line.Length == 0) return new MdLine { Block = MdStyle.CodeBlock, Tokens = [] };
        return new MdLine
        {
            Block = MdStyle.CodeBlock,
            Tokens = [new MdToken(0, line.Length, MdStyle.CodeBlock)],
        };
    }

    /// <summary>
    /// A table's delimiter row (<c>|:---|---:|</c>). It carries no prose, so it is treated like a
    /// horizontal rule: the characters fade out and a stroke is drawn where the row sits, giving
    /// the header a clean underline.
    /// </summary>
    public static MdLine ScanTableDelimiter(string line)
    {
        int i = SkipWhitespace(line, 0);
        int contentEnd = line.Length;
        while (contentEnd > i && (line[contentEnd - 1] == ' ' || line[contentEnd - 1] == '\t')) contentEnd--;

        var tokens = new List<MdToken>(1);
        if (contentEnd > i)
            tokens.Add(new MdToken(i, contentEnd - i, MdStyle.Marker | MdStyle.Rule | MdStyle.Table | MdStyle.TableDelimiter));

        return new MdLine
        {
            Block = MdStyle.Rule | MdStyle.Table | MdStyle.TableDelimiter,
            ContentStart = i,
            Tokens = tokens,
            AllMarkers = true,
        };
    }

    /// <summary>
    /// A table header or body row. Pipes become dim column separators; each cell is parsed for the
    /// usual inline markup so bold, code and links work inside cells. The header row is flagged so
    /// the colorizer can weight it.
    /// </summary>
    public static MdLine ScanTableRow(string line, bool header)
    {
        var tokens = new List<MdToken>(8);
        int i = SkipWhitespace(line, 0);

        // Collect the unescaped pipe positions so the first and last can be marked as table edges.
        var pipes = new List<int>(4);
        for (int p = i; p < line.Length; p++)
        {
            if (line[p] == '\\') { p++; continue; }
            if (line[p] == '|') pipes.Add(p);
        }

        int trimmedEnd = line.Length;
        while (trimmedEnd > i && (line[trimmedEnd - 1] == ' ' || line[trimmedEnd - 1] == '\t')) trimmedEnd--;

        int cellStart = i;
        for (int k = 0; k < pipes.Count; k++)
        {
            int pipe = pipes[k];
            if (pipe > cellStart) ParseInline(line, cellStart, pipe, MdStyle.None, tokens, 0);

            bool leadingEdge = k == 0 && pipe == i;
            bool trailingEdge = k == pipes.Count - 1 && pipe == trimmedEnd - 1;
            var style = MdStyle.Marker | MdStyle.Table | ((leadingEdge || trailingEdge) ? MdStyle.TableEdge : MdStyle.None);
            tokens.Add(new MdToken(pipe, 1, style));
            cellStart = pipe + 1;
        }
        if (cellStart < line.Length) ParseInline(line, cellStart, line.Length, MdStyle.None, tokens, 0);

        tokens.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        return new MdLine
        {
            Block = MdStyle.Table | (header ? MdStyle.TableHeader : MdStyle.None),
            ContentStart = i,
            Tokens = tokens,
        };
    }

    /// <summary>
    /// The text line of a setext heading (the line sitting above a <c>===</c> or <c>---</c> rule).
    /// Its prose is parsed for inline markup and the whole line is flagged as a heading of the
    /// given level so the colorizer weights and scales it like an ATX heading.
    /// </summary>
    public static MdLine ScanSetextHeading(string line, int level)
    {
        var tokens = new List<MdToken>(8);
        int i = SkipWhitespace(line, 0);
        ParseInline(line, i, line.Length, MdStyle.None, tokens, 0);

        return new MdLine
        {
            Block = MdStyle.Heading,
            HeadingLevel = level,
            ContentStart = i,
            Tokens = tokens,
        };
    }

    /// <summary>The <c>===</c>/<c>---</c> underline of a setext heading: drawn as a rule beneath the text.</summary>
    public static MdLine ScanSetextUnderline(string line)
    {
        int i = SkipWhitespace(line, 0);
        int end = line.Length;
        while (end > i && (line[end - 1] == ' ' || line[end - 1] == '\t')) end--;

        var tokens = new List<MdToken>(1);
        if (end > i) tokens.Add(new MdToken(i, end - i, MdStyle.Marker | MdStyle.Rule | MdStyle.Heading));

        return new MdLine
        {
            Block = MdStyle.Rule | MdStyle.Heading,
            ContentStart = i,
            Tokens = tokens,
            AllMarkers = true,
        };
    }

    public static MdLine Scan(string line)
    {
        if (line.Length == 0) return MdLine.Empty;

        var tokens = new List<MdToken>(8);
        var block = MdStyle.None;
        int headingLevel = 0;
        int quoteDepth = 0;

        int i = SkipWhitespace(line, 0);

        // ---- blockquote prefixes: "> ", ">> ", ... -------------------------------
        while (i < line.Length && line[i] == '>')
        {
            int start = i++;
            if (i < line.Length && line[i] == ' ') i++;
            tokens.Add(new MdToken(start, i - start, MdStyle.Marker | MdStyle.Quote));
            quoteDepth++;
            block |= MdStyle.Quote;
            i = SkipWhitespace(line, i);
        }

        // The quote bar anchors here — where prose begins after the "> " prefixes — even on a
        // callout line, whose label is part of that prose.
        int quoteContentStart = i;

        // ---- callout label: "> [!NOTE]" ----------------------------------------
        bool isCallout = false;
        if (quoteDepth > 0 && i + 1 < line.Length && line[i] == '[' && line[i + 1] == '!')
        {
            int close = line.IndexOf(']', i + 2);
            if (close > i + 2 && Callout.Parse(line.AsSpan(i + 2, close - (i + 2))) != CalloutKind.None)
            {
                block |= MdStyle.Callout;
                isCallout = true;
                tokens.Add(new MdToken(i, 2, MdStyle.Marker | MdStyle.Callout));
                tokens.Add(new MdToken(i + 2, close - (i + 2), MdStyle.Callout));
                tokens.Add(new MdToken(close, 1, MdStyle.Marker | MdStyle.Callout));
                i = SkipWhitespace(line, close + 1);
            }
        }

        // ---- horizontal rule ----------------------------------------------------
        if (IsHorizontalRule(line, i))
        {
            tokens.Add(new MdToken(i, line.Length - i, MdStyle.Marker | MdStyle.Rule));
            return new MdLine
            {
                Block = block | MdStyle.Rule,
                QuoteDepth = quoteDepth,
                ContentStart = i,
                Tokens = tokens,
                AllMarkers = true,
            };
        }

        // ---- ATX heading --------------------------------------------------------
        if (i < line.Length && line[i] == '#')
        {
            int hashes = 0;
            while (i + hashes < line.Length && line[i + hashes] == '#') hashes++;
            int after = i + hashes;
            bool valid = hashes is >= 1 and <= 6 &&
                         (after >= line.Length || line[after] == ' ' || line[after] == '\t');
            if (valid)
            {
                int markerEnd = after;
                while (markerEnd < line.Length && (line[markerEnd] == ' ' || line[markerEnd] == '\t')) markerEnd++;
                headingLevel = hashes;
                block |= MdStyle.Heading;
                tokens.Add(new MdToken(i, markerEnd - i, MdStyle.Marker | MdStyle.Heading));
                i = markerEnd;
            }
        }

        // ---- list item ----------------------------------------------------------
        int contentStart = isCallout ? quoteContentStart : i;
        if (headingLevel == 0)
        {
            int listEnd = MatchListMarker(line, i, out bool bullet);
            if (listEnd > i)
            {
                block |= MdStyle.ListMarker;
                var style = MdStyle.Marker | MdStyle.ListMarker | (bullet ? MdStyle.Bullet : MdStyle.None);
                // The trailing whitespace stays visible so nested indentation survives.
                int glyphEnd = i;
                while (glyphEnd < line.Length && line[glyphEnd] != ' ' && line[glyphEnd] != '\t') glyphEnd++;
                tokens.Add(new MdToken(i, glyphEnd - i, style));
                i = listEnd;
                contentStart = i;

                int taskEnd = MatchTaskBox(line, i, out bool checkedBox);
                if (taskEnd > i)
                {
                    block |= MdStyle.Task | (checkedBox ? MdStyle.TaskChecked : MdStyle.None);
                    tokens.Add(new MdToken(i, taskEnd - i,
                        MdStyle.Marker | MdStyle.Task | (checkedBox ? MdStyle.TaskChecked : MdStyle.None)));
                    i = taskEnd;
                    contentStart = i;
                }
            }
        }

        ParseInline(line, i, line.Length, MdStyle.None, tokens, 0);

        bool allMarkers = true;
        int covered = 0;
        foreach (var t in tokens)
        {
            covered += t.Length;
            if (!t.IsMarker && t.Length > 0) { allMarkers = false; break; }
        }
        if (allMarkers && covered < line.TrimEnd().Length) allMarkers = false;

        return new MdLine
        {
            Block = block,
            HeadingLevel = headingLevel,
            QuoteDepth = quoteDepth,
            ContentStart = contentStart,
            Tokens = tokens,
            AllMarkers = allMarkers && tokens.Count > 0,
        };
    }

    // -------------------------------------------------------------------------

    private const int MaxInlineDepth = 8;

    private static void ParseInline(string s, int start, int end, MdStyle inherit, List<MdToken> output, int depth)
    {
        if (start >= end) return;
        if (depth > MaxInlineDepth)
        {
            Emit(output, start, end - start, inherit);
            return;
        }

        int i = start;
        int plain = start;

        void Flush(int upto)
        {
            if (upto > plain && inherit != MdStyle.None) Emit(output, plain, upto - plain, inherit);
            plain = Math.Max(plain, upto);
        }

        while (i < end)
        {
            char c = s[i];

            switch (c)
            {
                case '\\' when i + 1 < end:
                    Flush(i);
                    output.Add(new MdToken(i, 1, MdStyle.Marker | inherit));
                    i += 2;
                    plain = i;
                    continue;

                case '`':
                {
                    int run = RunLength(s, i, end, '`');
                    int close = FindRun(s, i + run, end, '`', run, exact: true);
                    if (close < 0) break;
                    Flush(i);
                    output.Add(new MdToken(i, run, MdStyle.Marker | MdStyle.Code));
                    if (close > i + run) Emit(output, i + run, close - (i + run), inherit | MdStyle.Code);
                    output.Add(new MdToken(close, run, MdStyle.Marker | MdStyle.Code));
                    i = close + run;
                    plain = i;
                    continue;
                }

                case '*':
                case '_':
                {
                    if (c == '_' && i > 0 && IsWordChar(s[i - 1])) break;
                    int run = Math.Min(RunLength(s, i, end, c), 3);
                    int close = FindRun(s, i + run, end, c, run, exact: false);
                    if (close < 0 || close == i + run) break;
                    if (c == '_' && close + run < end && IsWordChar(s[close + run])) break;

                    var emphasis = run switch
                    {
                        1 => MdStyle.Italic,
                        2 => MdStyle.Bold,
                        _ => MdStyle.Bold | MdStyle.Italic,
                    };
                    Flush(i);
                    output.Add(new MdToken(i, run, MdStyle.Marker | inherit | emphasis));
                    ParseInline(s, i + run, close, inherit | emphasis, output, depth + 1);
                    output.Add(new MdToken(close, run, MdStyle.Marker | inherit | emphasis));
                    i = close + run;
                    plain = i;
                    continue;
                }

                case '~' when i + 1 < end && s[i + 1] == '~':
                {
                    int close = FindPair(s, i + 2, end, '~');
                    if (close < 0) break;
                    Flush(i);
                    output.Add(new MdToken(i, 2, MdStyle.Marker | inherit | MdStyle.Strike));
                    ParseInline(s, i + 2, close, inherit | MdStyle.Strike, output, depth + 1);
                    output.Add(new MdToken(close, 2, MdStyle.Marker | inherit | MdStyle.Strike));
                    i = close + 2;
                    plain = i;
                    continue;
                }

                case '=' when i + 1 < end && s[i + 1] == '=':
                {
                    int close = FindPair(s, i + 2, end, '=');
                    if (close < 0) break;
                    Flush(i);
                    output.Add(new MdToken(i, 2, MdStyle.Marker | inherit | MdStyle.Highlight));
                    ParseInline(s, i + 2, close, inherit | MdStyle.Highlight, output, depth + 1);
                    output.Add(new MdToken(close, 2, MdStyle.Marker | inherit | MdStyle.Highlight));
                    i = close + 2;
                    plain = i;
                    continue;
                }

                case '[' when i + 1 < end && s[i + 1] == '^':
                {
                    int close = FootnoteEnd(s, i + 2, end);
                    if (close < 0) break;
                    Flush(i);
                    output.Add(new MdToken(i, 2, MdStyle.Marker | MdStyle.Footnote));           // "[^"
                    Emit(output, i + 2, close - (i + 2), inherit | MdStyle.Footnote);            // the id
                    output.Add(new MdToken(close, 1, MdStyle.Marker | MdStyle.Footnote));        // "]"
                    i = close + 1;
                    plain = i;
                    continue;
                }

                case '!' when i + 1 < end && s[i + 1] == '[':
                case '[':
                {
                    Flush(i);
                    if (TryParseLink(s, i, end, inherit, output, depth, out int linkEnd))
                    {
                        i = linkEnd;
                        plain = i;
                        continue;
                    }
                    break;
                }

                case '<':
                {
                    int close = s.IndexOf('>', i + 1);
                    if (close < 0 || close >= end || !LooksLikeUri(s, i + 1, close)) break;
                    Flush(i);
                    output.Add(new MdToken(i, 1, MdStyle.Marker | MdStyle.Link));
                    Emit(output, i + 1, close - i - 1, inherit | MdStyle.Link);
                    output.Add(new MdToken(close, 1, MdStyle.Marker | MdStyle.Link));
                    i = close + 1;
                    plain = i;
                    continue;
                }
            }

            i++;
        }

        Flush(end);
    }

    private static bool TryParseLink(
        string s, int i, int end, MdStyle inherit, List<MdToken> output, int depth, out int linkEnd)
    {
        linkEnd = i;
        int bang = s[i] == '!' ? 1 : 0;
        int open = i + bang;

        int labelEnd = MatchBracket(s, open, end, '[', ']');
        if (labelEnd < 0) return false;
        if (labelEnd + 1 >= end || s[labelEnd + 1] != '(') return false;

        int urlEnd = MatchBracket(s, labelEnd + 1, end, '(', ')');
        if (urlEnd < 0) return false;

        var linkStyle = inherit | MdStyle.Link | (bang == 1 ? MdStyle.Image : MdStyle.None);

        output.Add(new MdToken(i, open + 1 - i, MdStyle.Marker | MdStyle.Link));
        if (labelEnd > open + 1)
            ParseInline(s, open + 1, labelEnd, linkStyle, output, depth + 1);
        output.Add(new MdToken(labelEnd, urlEnd + 1 - labelEnd, MdStyle.Marker | MdStyle.Url));

        linkEnd = urlEnd + 1;
        return true;
    }

    private static void Emit(List<MdToken> output, int offset, int length, MdStyle style)
    {
        if (length > 0 && style != MdStyle.None) output.Add(new MdToken(offset, length, style));
    }

    private static int SkipWhitespace(string s, int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return i;
    }

    private static int RunLength(string s, int i, int end, char c)
    {
        int n = 0;
        while (i + n < end && s[i + n] == c) n++;
        return n;
    }

    /// <summary>Finds the next run of <paramref name="c"/> of at least (or exactly) <paramref name="len"/>.</summary>
    private static int FindRun(string s, int from, int end, char c, int len, bool exact)
    {
        int i = from;
        while (i < end)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] != c) { i++; continue; }
            int run = RunLength(s, i, end, c);
            if (exact ? run == len : run >= len) return i;
            i += run;
        }
        return -1;
    }

    private static int FindPair(string s, int from, int end, char c)
    {
        for (int i = from; i + 1 < end; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == c && s[i + 1] == c) return i;
        }
        return -1;
    }

    /// <summary>Returns the index of the matching close bracket, honouring nesting.</summary>
    private static int MatchBracket(string s, int openIndex, int end, char open, char close)
    {
        int depth = 0;
        for (int i = openIndex; i < end; i++)
        {
            char ch = s[i];
            if (ch == '\\') { i++; continue; }
            if (ch == open) depth++;
            else if (ch == close && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Index of the <c>]</c> closing a <c>[^id]</c> footnote reference, or -1. The id must be
    /// non-empty and contain no whitespace or brackets.</summary>
    private static int FootnoteEnd(string s, int idStart, int end)
    {
        int i = idStart;
        while (i < end)
        {
            char c = s[i];
            if (c == ']') return i > idStart ? i : -1;
            if (char.IsWhiteSpace(c) || c == '[' || c == '^') return -1;
            i++;
        }
        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsHorizontalRule(string line, int from)
    {
        int i = SkipWhitespace(line, from);
        if (i >= line.Length) return false;
        char c = line[i];
        if (c is not ('-' or '*' or '_')) return false;

        int count = 0;
        for (; i < line.Length; i++)
        {
            if (line[i] == c) count++;
            else if (line[i] != ' ' && line[i] != '\t') return false;
        }
        return count >= 3;
    }

    private static int MatchListMarker(string line, int i, out bool bullet)
    {
        bullet = false;
        if (i >= line.Length) return i;

        char c = line[i];
        int j = i;

        if (c is '-' or '*' or '+')
        {
            bullet = true;
            j = i + 1;
        }
        else if (char.IsDigit(c))
        {
            int digits = 0;
            while (j < line.Length && char.IsDigit(line[j]) && digits < 9) { j++; digits++; }
            if (j >= line.Length || (line[j] != '.' && line[j] != ')')) return i;
            j++;
        }
        else
        {
            return i;
        }

        if (j >= line.Length) return j;
        if (line[j] != ' ' && line[j] != '\t') return i;
        while (j < line.Length && (line[j] == ' ' || line[j] == '\t')) j++;
        return j;
    }

    private static int MatchTaskBox(string line, int i, out bool isChecked)
    {
        isChecked = false;
        if (i + 2 >= line.Length || line[i] != '[' || line[i + 2] != ']') return i;

        char inner = line[i + 1];
        if (inner is 'x' or 'X') isChecked = true;
        else if (inner is not (' ' or '-')) return i;

        int j = i + 3;
        while (j < line.Length && (line[j] == ' ' || line[j] == '\t')) j++;
        return j;
    }

    private static bool LooksLikeUri(string s, int start, int end)
    {
        int colon = s.IndexOf(':', start);
        if (colon < 0 || colon >= end || colon == start) return false;
        for (int i = start; i < end; i++)
            if (char.IsWhiteSpace(s[i])) return false;
        return true;
    }
}
