using System.Text;
using App.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ProgressSequenceParserTests
{
    private const string TooLong = "\e]9;4;1;0000000000000000000000000000000000000000\a";

    [TestMethod]
    [DataRow("a\e]9;4;1;50\ab", DisplayName = "Ended by BEL")]
    [DataRow("a\e]9;4;1;50\e\\b", DisplayName = "Ended by ST")]
    public void ParseTakesSequenceOut(string text)
    {
        var parser = new ProgressSequenceParser();

        var output = Parse(parser, text);

        Assert.AreEqual("ab", output);
        Assert.AreEqual(new ProgressReport(ProgressState.Normal, 50), parser.Progress);
    }

    [TestMethod]
    [DataRow("\e[31mred\e[0m", DisplayName = "Color")]
    [DataRow("\e]9;5;1\a", DisplayName = "Other sequence")]
    [DataRow("\e]9;4;1;50\ex", DisplayName = "Interrupted")]
    [DataRow(TooLong, DisplayName = "Too long")]
    public void ParseKeepsOtherText(string text)
    {
        var parser = new ProgressSequenceParser();

        var output = Parse(parser, text);

        Assert.AreEqual(text, output);
        Assert.AreEqual(new ProgressReport(ProgressState.None, 0), parser.Progress);
    }

    [TestMethod]
    public void ParseTakesSequenceOfUnknownStateOut()
    {
        var parser = new ProgressSequenceParser();

        var output = Parse(parser, "a\e]9;4;7;50\ab");

        Assert.AreEqual("ab", output);
        Assert.AreEqual(new ProgressReport(ProgressState.None, 0), parser.Progress);
    }

    [TestMethod]
    public void ParseHoldsBackStartOfSequenceUntilFlush()
    {
        var parser = new ProgressSequenceParser();
        var output = new StringBuilder();

        parser.Parse("a\e]9;4;1", output);
        Assert.AreEqual("a", output.ToString());

        parser.Flush(output);
        Assert.AreEqual("a\e]9;4;1", output.ToString());
    }

    [TestMethod]
    [DataRow("before\e]9;4;1;42\aafter", "beforeafter", ProgressState.Normal, 42, DisplayName = "Ended by BEL")]
    [DataRow("before\e]9;4;1;42\e\\after", "beforeafter", ProgressState.Normal, 42, DisplayName = "Ended by ST")]
    [DataRow("before\e]9;4;1;42\exafter", "before\e]9;4;1;42\exafter", ProgressState.None, 0, DisplayName = "Interrupted")]
    [DataRow("before\e]9;4;1\e\e]9;4;1;10\aafter", "before\e]9;4;1\eafter", ProgressState.Normal, 10, DisplayName = "Interrupted by another")]
    [DataRow("before" + TooLong + "after", "before" + TooLong + "after", ProgressState.None, 0, DisplayName = "Too long")]
    [DataRow("before\e]9;4;1;42", "before\e]9;4;1;42", ProgressState.None, 0, DisplayName = "Not ended")]
    public void ParseDoesNotDependOnHowTextIsSplit(string text, string expected, ProgressState state, int value)
    {
        for (var split = 0; split <= text.Length; split++)
        {
            var parser = new ProgressSequenceParser();
            var output = new StringBuilder();

            parser.Parse(text.AsSpan(0, split), output);
            parser.Parse(text.AsSpan(split), output);
            parser.Flush(output);

            Assert.AreEqual(expected, output.ToString(), $"Split at {split}");
            Assert.AreEqual(new ProgressReport(state, value), parser.Progress, $"Split at {split}");
        }
    }

    // The bodies of the sequences reported, in order, separated by '|'.
    [TestMethod]
    [DataRow("1;40", ProgressState.Normal, 40)]
    [DataRow("1;40|1;0", ProgressState.Normal, 0)]
    [DataRow("1;150", ProgressState.Normal, 100)]
    [DataRow("1;40|2", ProgressState.Error, 40)]
    [DataRow("1;40|2;0", ProgressState.Error, 40)]
    [DataRow("1;40|2;60", ProgressState.Error, 60)]
    [DataRow("1;40|3", ProgressState.Indeterminate, 40)]
    [DataRow("1;40|4", ProgressState.Paused, 40)]
    [DataRow("1;40|4;70", ProgressState.Paused, 70)]
    [DataRow("1;40|0", ProgressState.None, 0)]
    [DataRow("1;40|7;60", ProgressState.Normal, 40)]
    [DataRow("1;40|x", ProgressState.Normal, 40)]
    public void ProgressIsWhatTaskbarShows(string bodies, ProgressState state, int value)
    {
        var parser = new ProgressSequenceParser();

        var output = Parse(parser, string.Concat(bodies.Split('|').Select(static body => $"\e]9;4;{body}\a")));

        Assert.AreEqual(string.Empty, output);
        Assert.AreEqual(new ProgressReport(state, value), parser.Progress);
    }

    private static string Parse(ProgressSequenceParser parser, string text)
    {
        var output = new StringBuilder();
        parser.Parse(text, output);
        return output.ToString();
    }
}
