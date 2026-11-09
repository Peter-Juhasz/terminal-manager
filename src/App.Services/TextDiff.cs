using System.Text.RegularExpressions;

namespace App.Services;

/// <summary>How a row of a diff differs between the two sides.</summary>
public enum DiffKind
{
    /// <summary>The line is on both sides, the same.</summary>
    Unchanged,

    /// <summary>The line is only on the left side.</summary>
    Deleted,

    /// <summary>The line is only on the right side.</summary>
    Inserted,

    /// <summary>A line of the left side was changed to one of the right side, in the parts in <see cref="DiffLine.Changes"/>.</summary>
    Modified,
}

/// <summary>A part of a line that was changed, in characters.</summary>
public readonly record struct DiffSpan(int Start, int Length);

/// <summary>A line of one side of a diff, with its 1-based number in that side, and the parts of it that were changed if it was modified.</summary>
public sealed record DiffLine(int Number, string Text, IReadOnlyList<DiffSpan> Changes);

/// <summary>
/// A row of a diff, laid out side by side: an unchanged or modified line has both sides, a deleted line only the left and an inserted line
/// only the right, so the rows of both sides line up.
/// </summary>
public sealed record DiffRow(DiffKind Kind, DiffLine? Left, DiffLine? Right);

/// <summary>The rows of a diff, which are approximate if the sides were too different to find the fewest changes between them.</summary>
public sealed record TextDiffResult(IReadOnlyList<DiffRow> Rows, bool IsApproximate)
{
    /// <summary>Whether the sides differ.</summary>
    public bool HasChanges => Rows.Any(static row => row.Kind is not DiffKind.Unchanged);
}

/// <summary>What is ignored when lines are compared.</summary>
public sealed record TextDiffOptions
{
    public static TextDiffOptions Default { get; } = new();

    /// <summary>Whether lines that differ only in case are the same.</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>Whether lines that differ only in the amount of whitespace, or in whitespace at their start or end, are the same.</summary>
    public bool IgnoreWhitespace { get; init; }

    /// <summary>
    /// The parts of lines that are ignored when they are compared, such as timestamps or durations, which would make every line of two runs
    /// of the same command differ. Lines that differ only in them are unchanged, and are shown as they are on each side.
    /// </summary>
    public IReadOnlyList<Regex> IgnoredPatterns { get; init; } = [];

    /// <summary>
    /// The most changes looked for between the lines that differ, past which they are taken as all of the left side replaced by all of the right,
    /// so comparing outputs that have little in common doesn't take long. It also bounds how much memory the diff takes, which grows with its square.
    /// </summary>
    public int MaxEditDistance { get; init; } = 1000;
}

/// <summary>
/// Compares lines of text, such as the outputs of two commands, by the fewest lines deleted and inserted to turn the left side into the right,
/// and the lines changed in place by the fewest words and other tokens, with the Myers diff algorithm.
/// </summary>
public static class TextDiff
{
    private enum EditOperation : byte
    {
        Equal,
        Delete,
        Insert,
    }

    // what the ignored parts of lines are replaced with, so lines that differ only in them have the same key
    private const string IgnoredPlaceholder = "￼";

    /// <summary>Splits <paramref name="text"/> into lines, where a line break at its end doesn't start another, empty line.</summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = [];
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            lines.Add(line.ToString());
        }

        if (lines is [.., ""])
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>Compares the lines of <paramref name="left"/> to those of <paramref name="right"/>.</summary>
    public static TextDiffResult Compare(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        TextDiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        options ??= TextDiffOptions.Default;

        // lines are compared by the ids of their keys, so the diff compares numbers instead of strings
        Dictionary<string, int> ids = new(options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var leftIds = ToLineIds(left, ids, options);
        var rightIds = ToLineIds(right, ids, options);

        List<EditOperation> operations = [];
        var isExact = Diff(leftIds, rightIds, options.MaxEditDistance, operations, cancellationToken);

        List<DiffRow> rows = new(Math.Max(left.Count, right.Count));
        var leftIndex = 0;
        var rightIndex = 0;
        var position = 0;
        while (position < operations.Count)
        {
            if (operations[position] is EditOperation.Equal)
            {
                rows.Add(new DiffRow(
                    Kind: DiffKind.Unchanged,
                    Left: new DiffLine(Number: leftIndex + 1, Text: left[leftIndex], Changes: []),
                    Right: new DiffLine(Number: rightIndex + 1, Text: right[rightIndex], Changes: [])));
                leftIndex++;
                rightIndex++;
                position++;
                continue;
            }

            // the lines changed up to the next unchanged one, where those deleted are paired with those inserted as modified, in order
            var deleted = 0;
            var inserted = 0;
            for (; position < operations.Count && operations[position] is not EditOperation.Equal; position++)
            {
                if (operations[position] is EditOperation.Delete)
                {
                    deleted++;
                }
                else
                {
                    inserted++;
                }
            }

            var paired = Math.Min(deleted, inserted);
            for (var offset = 0; offset < paired; offset++)
            {
                rows.Add(CompareLine(leftIndex + offset, left[leftIndex + offset], rightIndex + offset, right[rightIndex + offset], options, cancellationToken));
            }

            for (var offset = paired; offset < deleted; offset++)
            {
                rows.Add(new DiffRow(
                    Kind: DiffKind.Deleted,
                    Left: new DiffLine(Number: leftIndex + offset + 1, Text: left[leftIndex + offset], Changes: []),
                    Right: null));
            }

            for (var offset = paired; offset < inserted; offset++)
            {
                rows.Add(new DiffRow(
                    Kind: DiffKind.Inserted,
                    Left: null,
                    Right: new DiffLine(Number: rightIndex + offset + 1, Text: right[rightIndex + offset], Changes: [])));
            }

            leftIndex += deleted;
            rightIndex += inserted;
        }

        return new TextDiffResult(Rows: rows, IsApproximate: !isExact);
    }

    private static int[] ToLineIds(IReadOnlyList<string> lines, Dictionary<string, int> ids, TextDiffOptions options)
    {
        var result = new int[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var key = ToKey(lines[index], options);
            if (!ids.TryGetValue(key, out var id))
            {
                id = ids.Count;
                ids.Add(key, id);
            }

            result[index] = id;
        }

        return result;
    }

    private static string ToKey(string line, TextDiffOptions options)
    {
        foreach (var pattern in options.IgnoredPatterns)
        {
            line = pattern.Replace(line, IgnoredPlaceholder);
        }

        if (!options.IgnoreWhitespace)
        {
            return line;
        }

        return string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    // Compares the tokens of two lines that were paired as modified, where the parts of each that differ are its changes.
    private static DiffRow CompareLine(
        int leftIndex,
        string left,
        int rightIndex,
        string right,
        TextDiffOptions options,
        CancellationToken cancellationToken)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);

        Dictionary<string, int> ids = new(options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var lookup = ids.GetAlternateLookup<ReadOnlySpan<char>>();
        var leftIds = ToTokenIds(left, leftTokens, lookup, options);
        var rightIds = ToTokenIds(right, rightTokens, lookup, options);

        List<EditOperation> operations = [];
        Diff(leftIds, rightIds, options.MaxEditDistance, operations, cancellationToken);

        List<DiffSpan> leftChanges = [];
        List<DiffSpan> rightChanges = [];
        var leftToken = 0;
        var rightToken = 0;
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case EditOperation.Equal:
                    leftToken++;
                    rightToken++;
                    break;

                case EditOperation.Delete:
                    AddChange(leftChanges, leftTokens[leftToken++]);
                    break;

                case EditOperation.Insert:
                    AddChange(rightChanges, rightTokens[rightToken++]);
                    break;
            }
        }

        return new DiffRow(
            Kind: DiffKind.Modified,
            Left: new DiffLine(Number: leftIndex + 1, Text: left, Changes: leftChanges),
            Right: new DiffLine(Number: rightIndex + 1, Text: right, Changes: rightChanges));
    }

    // adjacent changed tokens are one change
    private static void AddChange(List<DiffSpan> changes, DiffSpan token)
    {
        if (changes is [.., var last] && last.Start + last.Length == token.Start)
        {
            changes[^1] = last with { Length = last.Length + token.Length };
            return;
        }

        changes.Add(token);
    }

    // Splits a line into words, runs of whitespace, and each other character on its own, so a change is shown in whole words.
    private static List<DiffSpan> Tokenize(string line)
    {
        List<DiffSpan> tokens = [];
        var position = 0;
        while (position < line.Length)
        {
            var start = position;
            var character = line[position++];
            if (IsWordCharacter(character))
            {
                while (position < line.Length && IsWordCharacter(line[position]))
                {
                    position++;
                }
            }
            else if (char.IsWhiteSpace(character))
            {
                while (position < line.Length && char.IsWhiteSpace(line[position]))
                {
                    position++;
                }
            }

            tokens.Add(new DiffSpan(Start: start, Length: position - start));
        }

        return tokens;

        static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character is '_';
    }

    private static int[] ToTokenIds(
        string line,
        List<DiffSpan> tokens,
        Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> ids,
        TextDiffOptions options)
    {
        // any amount of whitespace is the same when it is ignored, which no other token's id is
        const int WhitespaceId = -1;

        var result = new int[tokens.Count];
        for (var index = 0; index < tokens.Count; index++)
        {
            var text = line.AsSpan(tokens[index].Start, tokens[index].Length);
            if (options.IgnoreWhitespace && char.IsWhiteSpace(text[0]))
            {
                result[index] = WhitespaceId;
                continue;
            }

            if (!ids.TryGetValue(text, out var id))
            {
                id = ids.Dictionary.Count;
                ids[text] = id;
            }

            result[index] = id;
        }

        return result;
    }

    // Adds the operations that turn a into b to operations, the fewest of them unless there are more than maxEditDistance,
    // in which case all of what differs is deleted and inserted, and false is returned.
    private static bool Diff(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int maxEditDistance, List<EditOperation> operations, CancellationToken cancellationToken)
    {
        // the start and end in common are cheap to find, and two runs of the same command usually have most of their output in common
        var prefix = a.CommonPrefixLength(b);
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)])
        {
            suffix++;
        }

        operations.AddRange(Enumerable.Repeat(EditOperation.Equal, prefix));

        var middleOfA = a[prefix..^suffix];
        var middleOfB = b[prefix..^suffix];
        var isExact = TryDiff(middleOfA, middleOfB, maxEditDistance, operations, cancellationToken);
        if (!isExact)
        {
            operations.AddRange(Enumerable.Repeat(EditOperation.Delete, middleOfA.Length));
            operations.AddRange(Enumerable.Repeat(EditOperation.Insert, middleOfB.Length));
        }

        operations.AddRange(Enumerable.Repeat(EditOperation.Equal, suffix));
        return isExact;
    }

    // The greedy algorithm of Myers' "An O(ND) Difference Algorithm and Its Variations": for each number of edits d, how far along each
    // diagonal k = x - y the furthest path of d edits gets, until one reaches the end of both; the furthest of each diagonal is kept for
    // every d, which is O(D²) memory, so the path can be followed back from the end.
    private static bool TryDiff(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int maxEditDistance, List<EditOperation> operations, CancellationToken cancellationToken)
    {
        var n = a.Length;
        var m = b.Length;
        var max = n + m;

        // v[offset + k] is the furthest x on diagonal k, which is read for k ± 1 of every k from -d to d
        var offset = max + 1;
        var v = new int[2 * max + 3];
        List<int[]> trace = [];

        var limit = Math.Min(max, maxEditDistance);
        for (var d = 0; d <= limit; d++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // what the furthest x were after d - 1 edits, from diagonal -d - 1 to d + 1
            trace.Add(v.AsSpan(offset - d - 1, 2 * d + 3).ToArray());
            for (var k = -d; k <= d; k += 2)
            {
                // down from diagonal k + 1, which is an insertion, or right from diagonal k - 1, which is a deletion, whichever is further
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    Backtrack(trace, n, m, operations);
                    return true;
                }
            }
        }

        return false;
    }

    private static void Backtrack(List<int[]> trace, int n, int m, List<EditOperation> operations)
    {
        var start = operations.Count;
        var x = n;
        var y = m;
        for (var d = trace.Count - 1; d > 0; d--)
        {
            // trace[d] holds diagonals -d - 1 to d + 1
            var v = trace[d];
            var offset = d + 1;
            var k = x - y;
            var previousK = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? k + 1 : k - 1;
            var previousX = v[offset + previousK];
            var previousY = previousX - previousK;
            while (x > previousX && y > previousY)
            {
                operations.Add(EditOperation.Equal);
                x--;
                y--;
            }

            operations.Add(x == previousX ? EditOperation.Insert : EditOperation.Delete);
            x = previousX;
            y = previousY;
        }

        // the lines in common before the first edit
        while (x > 0 && y > 0)
        {
            operations.Add(EditOperation.Equal);
            x--;
            y--;
        }

        operations.Reverse(start, operations.Count - start);
    }
}
