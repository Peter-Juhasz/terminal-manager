using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Finds which character of a text is at a point, and where a character is, in the coordinates of the element that shows it.
/// </summary>
/// <remarks>
/// The characters of a <see cref="RichTextBlock"/> are counted the way the ranges of a highlighter count them, the runs of the paragraphs one after the other,
/// rather than by the offsets of its text pointers, which count where each element starts and ends too.
/// </remarks>
internal static class TextHitTesting
{
    extension(RichTextBlock block)
    {
        /// <summary>The text as its characters are counted: the runs of the paragraphs one after the other.</summary>
        public string GetPlainText() => string.Concat(block.GetRuns().Select(static run => run.Text));

        /// <summary>The index of the character at <paramref name="point"/>, or <see langword="null"/> when there is none there, such as past the end of a line.</summary>
        public int? GetCharacterIndexFromPoint(Point point)
        {
            if (block.GetPositionFromPoint(point) is not { Parent: Run hit } pointer)
            {
                return null;
            }

            var start = 0;
            foreach (var run in block.GetRuns())
            {
                if (!ReferenceEquals(run, hit))
                {
                    start += run.Text.Length;
                    continue;
                }

                // the nearest position can be at either edge of the character at the point
                var index = start + pointer.Offset - run.ContentStart.Offset;
                foreach (var candidate in (ReadOnlySpan<int>)[index, index - 1])
                {
                    if (block.GetCharacterBounds(candidate) is { } bounds && bounds.Contains(point))
                    {
                        return candidate;
                    }
                }

                return null;
            }

            return null;
        }

        /// <summary>Where the character at <paramref name="index"/> is laid out, or <see langword="null"/> when there is none at it.</summary>
        public Rect? GetCharacterBounds(int index)
        {
            if (index < 0)
            {
                return null;
            }

            var start = 0;
            foreach (var run in block.GetRuns())
            {
                var length = run.Text.Length;
                if (index >= start + length)
                {
                    start += length;
                    continue;
                }

                var offset = index - start;
                var leading = run.ContentStart.GetPositionAtOffset(offset, LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward);
                var trailing = run.ContentStart.GetPositionAtOffset(offset + 1, LogicalDirection.Backward).GetCharacterRect(LogicalDirection.Backward);
                return Bounds(leading, trailing);
            }

            return null;
        }

        /// <summary>The runs its characters are counted in, those of the paragraphs one after the other, with those in spans such as hyperlinks.</summary>
        public IEnumerable<Run> GetRuns() => block.Blocks.OfType<Paragraph>().SelectMany(static paragraph => GetRuns(paragraph.Inlines));
    }

    private static IEnumerable<Run> GetRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    yield return run;
                    break;

                case Span span:
                    foreach (var run in GetRuns(span.Inlines))
                    {
                        yield return run;
                    }

                    break;
            }
        }
    }

    extension(TextBox box)
    {
        /// <summary>The index of the character at <paramref name="point"/>, or <see langword="null"/> when there is none there, such as past the end of a line.</summary>
        /// <remarks>A text box can't tell it, but it can tell where each character is, which are in the order they are read, so the one at the point is searched for by halves.</remarks>
        public int? GetCharacterIndexFromPoint(Point point)
        {
            var low = 0;
            var high = box.Text.Length - 1;
            var found = -1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                var bounds = box.GetCharacterBounds(middle);

                // whether the character starts before the point, on a line above, or on its line and to the left of it
                if (bounds.Bottom <= point.Y || (bounds.Top <= point.Y && bounds.Left <= point.X))
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return found >= 0 && box.GetCharacterBounds(found).Contains(point) ? found : null;
        }

        /// <summary>Where the character at <paramref name="index"/> is laid out.</summary>
        public Rect GetCharacterBounds(int index) =>
            Bounds(box.GetCharacterEdge(index, trailingEdge: false), box.GetCharacterEdge(index, trailingEdge: true));

        /// <summary>The leading or the trailing edge of the character at <paramref name="index"/>, in the coordinates of the box.</summary>
        public Rect GetCharacterEdge(int index, bool trailingEdge)
        {
            var edge = box.GetRectFromCharacterIndex(index, trailingEdge);

            // the rectangles are relative to where the text starts, inside the border and the padding, when the first character isn't as far in as that
            var textLeft = box.BorderThickness.Left + box.Padding.Left;
            var textTop = box.BorderThickness.Top + box.Padding.Top;
            return box.GetRectFromCharacterIndex(0, trailingEdge: false).X < textLeft / 2
                ? new(edge.X + textLeft, edge.Y + textTop, edge.Width, edge.Height)
                : edge;
        }
    }

    // From the leading edge of a character to its trailing one; when the edges are reported without a width, and the trailing one is on the next line, as wide as the leading one says.
    private static Rect Bounds(Rect leading, Rect trailing) =>
        Math.Abs(trailing.Y - leading.Y) < leading.Height / 2 && trailing.X > leading.X
            ? new(leading.X, leading.Y, trailing.X - leading.X, leading.Height)
            : leading;
}
