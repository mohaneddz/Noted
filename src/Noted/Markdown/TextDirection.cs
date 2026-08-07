namespace Noted.Markdown;

/// <summary>
/// Cheap base-direction detection for a note. AvalonEdit's paragraph direction is control-wide rather
/// than per line, so the editor picks one base direction for the whole document: right-to-left when
/// strong RTL letters (Arabic, Hebrew, …) outnumber strong LTR ones, otherwise left-to-right. Either
/// way WPF still shapes and orders mixed runs within a line correctly via the Unicode bidi algorithm.
/// </summary>
public static class TextDirection
{
    // Scanning the whole document on every keystroke would be wasteful; the base direction is decided
    // from a prefix, which is more than enough to classify a note.
    private const int SampleLength = 8000;

    public static bool IsPredominantlyRtl(string text)
    {
        int rtl = 0, ltr = 0;
        int limit = Math.Min(text.Length, SampleLength);
        for (int i = 0; i < limit; i++)
        {
            char c = text[i];
            if (IsStrongRtl(c)) rtl++;
            else if (IsStrongLtr(c)) ltr++;
        }
        return rtl > ltr;
    }

    private static bool IsStrongRtl(char c) =>
        c is >= '֐' and <= '׿'    // Hebrew
        or >= '؀' and <= 'ۿ'      // Arabic
        or >= 'ݐ' and <= 'ݿ'      // Arabic Supplement
        or >= 'ࢠ' and <= 'ࣿ'      // Arabic Extended-A
        or >= 'יִ' and <= '﷿'      // Hebrew + Arabic presentation forms A
        or >= 'ﹰ' and <= '﻿';     // Arabic presentation forms B

    private static bool IsStrongLtr(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
        or >= 'À' and <= 'ɏ';     // Latin-1 Supplement + Latin Extended-A/B letters
}
