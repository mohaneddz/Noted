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
    /// <summary>Length of a line's leading single-level blockquote marker ("&gt; "), or 0 if it has none.
    /// Lets the multi-line, whole-document constructs (fences, tables) — which otherwise scan raw,
    /// un-stripped line text — nest one level inside a blockquote instead of only being recognised as
    /// literal quoted prose.</summary>
    public static int QuotePrefixLength(string text)
    {
        int i = SkipWhitespace(text, 0);
        if (i >= text.Length || text[i] != '>') return 0;
        i++;
        if (i < text.Length && text[i] == ' ') i++;
        return i;
    }

    public static MdLine ScanFenceDelimiter(string line, int quotePrefixLength = 0)
    {
        if (quotePrefixLength <= 0)
        {
            return new MdLine
            {
                Block = MdStyle.CodeBlock,
                Tokens = [new MdToken(0, line.Length, MdStyle.Marker | MdStyle.CodeBlock)],
                AllMarkers = true,
            };
        }

        var tokens = new List<MdToken>(2) { new MdToken(0, quotePrefixLength, MdStyle.Marker | MdStyle.Quote) };
        if (line.Length > quotePrefixLength)
            tokens.Add(new MdToken(quotePrefixLength, line.Length - quotePrefixLength, MdStyle.Marker | MdStyle.CodeBlock));

        return new MdLine
        {
            Block = MdStyle.CodeBlock | MdStyle.Quote,
            QuoteDepth = 1,
            ContentStart = quotePrefixLength,
            Tokens = tokens,
            AllMarkers = true,
        };
    }

    public static MdLine ScanFencedContent(string line, int quotePrefixLength = 0)
    {
        if (quotePrefixLength <= 0)
        {
            if (line.Length == 0) return new MdLine { Block = MdStyle.CodeBlock, Tokens = [] };
            return new MdLine
            {
                Block = MdStyle.CodeBlock,
                Tokens = [new MdToken(0, line.Length, MdStyle.CodeBlock)],
            };
        }

        var tokens = new List<MdToken>(2) { new MdToken(0, quotePrefixLength, MdStyle.Marker | MdStyle.Quote) };
        if (line.Length > quotePrefixLength)
            tokens.Add(new MdToken(quotePrefixLength, line.Length - quotePrefixLength, MdStyle.CodeBlock));

        return new MdLine
        {
            Block = MdStyle.CodeBlock | MdStyle.Quote,
            QuoteDepth = 1,
            ContentStart = quotePrefixLength,
            Tokens = tokens,
        };
    }

    /// <summary>
    /// A <c>&lt;details&gt;</c>/<c>&lt;summary&gt;…&lt;/summary&gt;</c>/<c>&lt;/details&gt;</c> tag line.
    /// When the block is unfolded (caret inside it, editing the raw HTML) these wrapper tags fade to
    /// dim text — the same treatment a fence delimiter gets — so what's left reading like a "proper
    /// block" is the markdown body between them, not raw angle-bracket tags.
    /// </summary>
    public static MdLine ScanDetailsTag(string line)
    {
        int i = SkipWhitespace(line, 0);
        int end = line.Length;
        while (end > i && (line[end - 1] == ' ' || line[end - 1] == '\t')) end--;

        var tokens = new List<MdToken>(1);
        if (end > i) tokens.Add(new MdToken(i, end - i, MdStyle.Marker));

        return new MdLine
        {
            ContentStart = i,
            Tokens = tokens,
            AllMarkers = true,
        };
    }

    /// <summary>
    /// A fenced directive's opening (<c>::: note</c>) or closing (<c>:::</c>) line. The whole line
    /// is a marker — same treatment as a code-fence delimiter — so it fades to dim text instead of
    /// rendering as prose.
    /// </summary>
    public static MdLine ScanDirectiveDelimiter(string line)
    {
        int i = SkipWhitespace(line, 0);
        int end = line.Length;
        while (end > i && (line[end - 1] == ' ' || line[end - 1] == '\t')) end--;

        var tokens = new List<MdToken>(1);
        if (end > i) tokens.Add(new MdToken(i, end - i, MdStyle.Marker | MdStyle.Callout));

        return new MdLine
        {
            Block = MdStyle.Callout,
            ContentStart = i,
            Tokens = tokens,
            AllMarkers = true,
        };
    }

    /// <summary>Copies a scanned line, adding the <see cref="MdStyle.Callout"/> block flag — used for a
    /// fenced directive's body lines, whose prose is scanned normally but still needs the flag so the
    /// tinted frame keeps drawing across it (including blank lines).</summary>
    public static MdLine WithCalloutBlock(MdLine line) => new()
    {
        Block = line.Block | MdStyle.Callout,
        HeadingLevel = line.HeadingLevel,
        QuoteDepth = line.QuoteDepth,
        ContentStart = line.ContentStart,
        Tokens = line.Tokens,
        AllMarkers = line.AllMarkers,
    };

    /// <summary>
    /// A table's delimiter row (<c>|:---|---:|</c>). It carries no prose, so it is treated like a
    /// horizontal rule: the characters fade out and a stroke is drawn where the row sits, giving
    /// the header a clean underline.
    /// </summary>
    public static MdLine ScanTableDelimiter(string line, int quotePrefixLength = 0)
    {
        int i = SkipWhitespace(line, quotePrefixLength);
        int contentEnd = line.Length;
        while (contentEnd > i && (line[contentEnd - 1] == ' ' || line[contentEnd - 1] == '\t')) contentEnd--;

        var tokens = new List<MdToken>(2);
        if (quotePrefixLength > 0) tokens.Add(new MdToken(0, quotePrefixLength, MdStyle.Marker | MdStyle.Quote));
        if (contentEnd > i)
            tokens.Add(new MdToken(i, contentEnd - i, MdStyle.Marker | MdStyle.Rule | MdStyle.Table | MdStyle.TableDelimiter));

        return new MdLine
        {
            Block = MdStyle.Rule | MdStyle.Table | MdStyle.TableDelimiter | (quotePrefixLength > 0 ? MdStyle.Quote : MdStyle.None),
            QuoteDepth = quotePrefixLength > 0 ? 1 : 0,
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
    public static MdLine ScanTableRow(string line, bool header, IReadOnlySet<string>? refs = null, int quotePrefixLength = 0)
    {
        var tokens = new List<MdToken>(8);
        if (quotePrefixLength > 0) tokens.Add(new MdToken(0, quotePrefixLength, MdStyle.Marker | MdStyle.Quote));
        int i = SkipWhitespace(line, quotePrefixLength);

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
            if (pipe > cellStart) ParseInline(line, cellStart, pipe, MdStyle.None, tokens, 0, refs);

            bool leadingEdge = k == 0 && pipe == i;
            bool trailingEdge = k == pipes.Count - 1 && pipe == trimmedEnd - 1;
            var style = MdStyle.Marker | MdStyle.Table | ((leadingEdge || trailingEdge) ? MdStyle.TableEdge : MdStyle.None);
            tokens.Add(new MdToken(pipe, 1, style));
            cellStart = pipe + 1;
        }
        if (cellStart < line.Length) ParseInline(line, cellStart, line.Length, MdStyle.None, tokens, 0, refs);

        tokens.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        return new MdLine
        {
            Block = MdStyle.Table | (header ? MdStyle.TableHeader : MdStyle.None) |
                    (quotePrefixLength > 0 ? MdStyle.Quote : MdStyle.None),
            QuoteDepth = quotePrefixLength > 0 ? 1 : 0,
            ContentStart = i,
            Tokens = tokens,
        };
    }

    /// <summary>
    /// The text line of a setext heading (the line sitting above a <c>===</c> or <c>---</c> rule).
    /// Its prose is parsed for inline markup and the whole line is flagged as a heading of the
    /// given level so the colorizer weights and scales it like an ATX heading.
    /// </summary>
    public static MdLine ScanSetextHeading(string line, int level, IReadOnlySet<string>? refs = null)
    {
        var tokens = new List<MdToken>(8);
        int i = SkipWhitespace(line, 0);
        ParseInline(line, i, line.Length, MdStyle.None, tokens, 0, refs);

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

    public static MdLine Scan(string line, IReadOnlySet<string>? refs = null)
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

        ParseInline(line, i, line.Length, MdStyle.None, tokens, 0, refs);

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

    private static void ParseInline(string s, int start, int end, MdStyle inherit, List<MdToken> output, int depth,
        IReadOnlySet<string>? refs = null)
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
                    ParseInline(s, i + run, close, inherit | emphasis, output, depth + 1, refs);
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
                    ParseInline(s, i + 2, close, inherit | MdStyle.Strike, output, depth + 1, refs);
                    output.Add(new MdToken(close, 2, MdStyle.Marker | inherit | MdStyle.Strike));
                    i = close + 2;
                    plain = i;
                    continue;
                }

                case '~':
                {
                    int close = FindRun(s, i + 1, end, '~', 1, exact: true);
                    if (close < 0 || close == i + 1) break;
                    Flush(i);
                    output.Add(new MdToken(i, 1, MdStyle.Marker | inherit | MdStyle.Sub));
                    Emit(output, i + 1, close - (i + 1), inherit | MdStyle.Sub);
                    output.Add(new MdToken(close, 1, MdStyle.Marker | inherit | MdStyle.Sub));
                    i = close + 1;
                    plain = i;
                    continue;
                }

                case '^':
                {
                    int close = FindRun(s, i + 1, end, '^', 1, exact: true);
                    if (close < 0 || close == i + 1) break;
                    Flush(i);
                    output.Add(new MdToken(i, 1, MdStyle.Marker | inherit | MdStyle.Sup));
                    Emit(output, i + 1, close - (i + 1), inherit | MdStyle.Sup);
                    output.Add(new MdToken(close, 1, MdStyle.Marker | inherit | MdStyle.Sup));
                    i = close + 1;
                    plain = i;
                    continue;
                }

                case '=' when i + 1 < end && s[i + 1] == '=':
                {
                    int close = FindPair(s, i + 2, end, '=');
                    if (close < 0) break;
                    Flush(i);
                    output.Add(new MdToken(i, 2, MdStyle.Marker | inherit | MdStyle.Highlight));
                    ParseInline(s, i + 2, close, inherit | MdStyle.Highlight, output, depth + 1, refs);
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
                    if (TryParseLink(s, i, end, inherit, output, depth, refs, out int linkEnd) ||
                        TryParseReference(s, i, end, inherit, output, depth, refs, out linkEnd))
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
                    if (close < 0 || close >= end ||
                        !(LooksLikeUri(s, i + 1, close) || LooksLikeEmail(s, i + 1, close))) break;
                    Flush(i);
                    output.Add(new MdToken(i, 1, MdStyle.Marker | MdStyle.Link));
                    // Both Link and Url are set on the autolink's own content (unlike "[text](url)",
                    // where they live on separate tokens) so a click handler can tell the two apart
                    // and use the content itself as the destination.
                    Emit(output, i + 1, close - i - 1, inherit | MdStyle.Link | MdStyle.Url);
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
        string s, int i, int end, MdStyle inherit, List<MdToken> output, int depth, IReadOnlySet<string>? refs, out int linkEnd)
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
            ParseInline(s, open + 1, labelEnd, linkStyle, output, depth + 1, refs);
        output.Add(new MdToken(labelEnd, urlEnd + 1 - labelEnd, MdStyle.Marker | MdStyle.Url));

        linkEnd = urlEnd + 1;
        return true;
    }

    /// <summary>Parses a reference link/image: full <c>[text][id]</c>, collapsed <c>[text][]</c>, or shortcut
    /// <c>[id]</c> — but only when <paramref name="id"/> matches a known reference definition, so ordinary
    /// bracketed prose is left alone. The visible label shows as a link; the <c>[id]</c> tail is hidden.</summary>
    private static bool TryParseReference(
        string s, int i, int end, MdStyle inherit, List<MdToken> output, int depth, IReadOnlySet<string>? refs, out int linkEnd)
    {
        linkEnd = i;
        if (refs is null || refs.Count == 0) return false;

        int bang = s[i] == '!' ? 1 : 0;
        int open = i + bang;
        if (open >= end || s[open] != '[') return false;

        int labelEnd = MatchBracket(s, open, end, '[', ']');
        if (labelEnd < 0) return false;

        string text = s.Substring(open + 1, labelEnd - (open + 1));

        string refId;
        int constructEnd;   // exclusive end of the whole reference
        if (labelEnd + 1 < end && s[labelEnd + 1] == '[')
        {
            int secondEnd = MatchBracket(s, labelEnd + 1, end, '[', ']');
            if (secondEnd < 0) return false;
            string second = s.Substring(labelEnd + 2, secondEnd - (labelEnd + 2));
            refId = second.Trim().Length == 0 ? text : second;   // collapsed [text][] reuses the text
            constructEnd = secondEnd + 1;
        }
        else
        {
            if (labelEnd + 1 < end && s[labelEnd + 1] == '(') return false;   // an inline link, handled elsewhere
            refId = text;                                                     // shortcut [id]
            constructEnd = labelEnd + 1;
        }

        if (text.Trim().Length == 0 || !refs.Contains(NormalizeReferenceLabel(refId))) return false;

        var linkStyle = inherit | MdStyle.Link | (bang == 1 ? MdStyle.Image : MdStyle.None);
        output.Add(new MdToken(i, open + 1 - i, MdStyle.Marker | MdStyle.Link));       // "[" or "!["
        ParseInline(s, open + 1, labelEnd, linkStyle, output, depth + 1, refs);        // the shown label
        output.Add(new MdToken(labelEnd, constructEnd - labelEnd, MdStyle.Marker | MdStyle.Url));  // "]" (+ "[id]")

        linkEnd = constructEnd;
        return true;
    }

    /// <summary>If the line is a link reference definition (<c>[label]: destination</c>), returns its raw label.
    /// Footnote definitions (<c>[^id]:</c>) are excluded.</summary>
    public static bool TryReadReferenceDefinition(string line, out string label)
    {
        label = string.Empty;
        int i = SkipWhitespace(line, 0);
        if (i >= line.Length || line[i] != '[') return false;
        if (i + 1 < line.Length && line[i + 1] == '^') return false;

        int close = -1;
        for (int k = i + 1; k < line.Length; k++)
        {
            if (line[k] == '\\') { k++; continue; }
            if (line[k] == '[') return false;
            if (line[k] == ']') { close = k; break; }
        }
        if (close <= i + 1) return false;                                 // empty label

        int colon = close + 1;
        if (colon >= line.Length || line[colon] != ':') return false;
        if (SkipWhitespace(line, colon + 1) >= line.Length) return false; // needs a destination

        label = line.Substring(i + 1, close - (i + 1));
        return true;
    }

    /// <summary>Splits a table row into its cell texts, honouring <c>\|</c> escapes and dropping the empty
    /// cells produced by optional leading/trailing pipes. Cell text keeps its escapes for the inline parser.</summary>
    public static List<string> SplitTableCells(string line)
    {
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length) { sb.Append(c).Append(line[i + 1]); i++; continue; }
            if (c == '|') { cells.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        cells.Add(sb.ToString());

        if (cells.Count > 0 && cells[0].Trim().Length == 0) cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Trim().Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    /// <summary>Reads a table's per-column alignment from its <c>:---</c> / <c>:--:</c> / <c>---:</c> delimiter row.</summary>
    public static ColumnAlign[] ParseColumnAligns(string delimiter)
    {
        var cells = SplitTableCells(delimiter);
        var result = new ColumnAlign[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            string c = cells[i].Trim();
            bool left = c.StartsWith(':'), right = c.EndsWith(':');
            result[i] = (left, right) switch
            {
                (true, true) => ColumnAlign.Center,
                (true, false) => ColumnAlign.Left,
                (false, true) => ColumnAlign.Right,
                _ => ColumnAlign.None,
            };
        }
        return result;
    }

    /// <summary>Normalises a reference label for matching: trim, collapse internal whitespace, case-fold.</summary>
    public static string NormalizeReferenceLabel(string label)
    {
        var sb = new System.Text.StringBuilder(label.Length);
        bool pendingSpace = false;
        foreach (char ch in label.Trim())
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = true; continue; }
            if (pendingSpace && sb.Length > 0) sb.Append(' ');
            pendingSpace = false;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>Styles a link reference definition line so it recedes as dim metadata.</summary>
    public static MdLine ScanReferenceDefinition(string line)
    {
        int i = SkipWhitespace(line, 0);
        var tokens = new List<MdToken>(1);
        if (line.Length > i)
            tokens.Add(new MdToken(i, line.Length - i, MdStyle.Marker | MdStyle.Url));

        return new MdLine
        {
            ContentStart = i,
            Tokens = tokens,
            AllMarkers = true,
        };
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

    /// <summary>True if <c>start..end</c> reads as a bare email address (for an <c>&lt;user@host&gt;</c> autolink):
    /// a single unescaped <c>@</c> with non-empty, whitespace-free sides and a dot in the domain.</summary>
    private static bool LooksLikeEmail(string s, int start, int end)
    {
        int at = -1;
        for (int i = start; i < end; i++)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) return false;
            if (c == '@')
            {
                if (at >= 0) return false;   // more than one '@'
                at = i;
            }
        }
        if (at <= start || at >= end - 1) return false;      // '@' must have text on both sides
        int dot = s.IndexOf('.', at + 1);
        return dot > at + 1 && dot < end - 1;                // domain has a dot with labels around it
    }
}
