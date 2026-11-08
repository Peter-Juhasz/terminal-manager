using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class UrlDetectorTests
{
    private static string[] FindAll(string text) =>
        [.. UrlDetector.FindAll(text).Select(match => text.Substring(match.Start, match.Length))];

    [TestMethod]
    [DataRow("https://example.com", "https://example.com")]
    [DataRow("see http://example.com/a?b=c#d here", "http://example.com/a?b=c#d")]
    [DataRow("HTTPS://Example.com", "HTTPS://Example.com")]
    [DataRow("url=https://example.com", "https://example.com")]
    public void FindsTheAddress(string text, string expected) => Assert.AreSequenceEqual(new[] { expected }, FindAll(text));

    [TestMethod]
    [DataRow("Go to https://example.com.", "https://example.com")]
    [DataRow("At https://example.com/a, then", "https://example.com/a")]
    [DataRow("Really https://example.com?!", "https://example.com")]
    public void LeavesOutThePunctuationAfterIt(string text, string expected) => Assert.AreSequenceEqual(new[] { expected }, FindAll(text));

    [TestMethod]
    [DataRow("(see https://example.com)", "https://example.com")]
    [DataRow("(https://en.wikipedia.org/wiki/A_(b))", "https://en.wikipedia.org/wiki/A_(b)")]
    [DataRow("[https://example.com/x]", "https://example.com/x")]
    [DataRow("\"https://example.com\"", "https://example.com")]
    [DataRow("'https://example.com'", "https://example.com")]
    [DataRow("<https://example.com>", "https://example.com")]
    public void EndsWithinTheBracketsOrQuotesItIsWrittenIn(string text, string expected) => Assert.AreSequenceEqual(new[] { expected }, FindAll(text));

    [TestMethod]
    public void EndsAtAnEscapeSequence() => Assert.AreSequenceEqual(new[] { "https://example.com" }, FindAll("https://example.com\u001b[0m"));

    [TestMethod]
    public void EndsAtALineBreak() => Assert.AreSequenceEqual(new[] { "https://example.com" }, FindAll("https://example.com\r\nnext"));

    [TestMethod]
    public void FindsEachAddressInOrder() =>
        Assert.AreSequenceEqual(new[] { "http://a.com", "https://b.com/x" }, FindAll("1 http://a.com 2 https://b.com/x 3"));

    [TestMethod]
    public void TakesAnAddressWrittenInAnotherAsPartOfIt() =>
        Assert.AreSequenceEqual(new[] { "https://a.com/?u=https://b.com" }, FindAll("https://a.com/?u=https://b.com"));

    [TestMethod]
    [DataRow("https://")]
    [DataRow("ftp://example.com")]
    [DataRow("file:///C:/temp")]
    [DataRow("example.com")]
    [DataRow("httpx://example.com")]
    [DataRow("")]
    public void FindsNothingButAddresses(string text) => Assert.IsEmpty(FindAll(text));

    [TestMethod]
    public void FindsNoMoreThanAsked() => Assert.HasCount(2, UrlDetector.FindAll("http://a.com http://b.com http://c.com", maxCount: 2));

    [TestMethod]
    public void TellsTheAddress()
    {
        var match = UrlDetector.FindAll("open https://example.com/a now").Single();

        Assert.AreEqual(new Uri("https://example.com/a"), match.Uri);
        Assert.AreEqual(5, match.Start);
        Assert.AreEqual(21, match.Length);
    }
}
