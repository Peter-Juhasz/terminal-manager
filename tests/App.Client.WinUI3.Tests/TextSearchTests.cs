using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class TextSearchTests
{
    [TestMethod]
    public void CreateOfNothingIsNull()
    {
        Assert.IsNull(TextSearch.Create(null, isCaseSensitive: false, isRegex: false));
        Assert.IsNull(TextSearch.Create(string.Empty, isCaseSensitive: false, isRegex: true));
    }

    [TestMethod]
    public void CreateOfInvalidRegexIsNull() => Assert.IsNull(TextSearch.Create("(", isCaseSensitive: false, isRegex: true));

    [TestMethod]
    public void SearchesForTheSameAreEqual()
    {
        var search = TextSearch.Create("a", isCaseSensitive: false, isRegex: false);

        Assert.AreEqual(search, TextSearch.Create("a", isCaseSensitive: false, isRegex: false));
        Assert.AreNotEqual(search, TextSearch.Create("b", isCaseSensitive: false, isRegex: false));
        Assert.AreNotEqual(search, TextSearch.Create("a", isCaseSensitive: true, isRegex: false));
        Assert.AreNotEqual(search, TextSearch.Create("a", isCaseSensitive: false, isRegex: true));
    }

    [TestMethod]
    public void PlainSearchIgnoresCaseUnlessCaseSensitive()
    {
        Assert.IsTrue(TextSearch.Create("error", isCaseSensitive: false, isRegex: false)!.IsMatch("An ERROR occurred"));
        Assert.IsFalse(TextSearch.Create("error", isCaseSensitive: true, isRegex: false)!.IsMatch("An ERROR occurred"));
    }

    [TestMethod]
    public void PlainSearchIsNotARegex() => Assert.IsFalse(TextSearch.Create("a.c", isCaseSensitive: false, isRegex: false)!.IsMatch("abc"));

    [TestMethod]
    public void FindMatchesOfPlainSearchDoesNotOverlap()
    {
        List<(int Start, int Length)> matches = [];
        TextSearch.Create("aa", isCaseSensitive: false, isRegex: false)!.FindMatches("aaaaa", matches);

        Assert.AreSequenceEqual(new[] { (0, 2), (2, 2) }, matches);
    }

    [TestMethod]
    public void FindMatchesOfRegexLeavesOutEmptyMatches()
    {
        List<(int Start, int Length)> matches = [];
        TextSearch.Create("b*", isCaseSensitive: false, isRegex: true)!.FindMatches("abba", matches);

        Assert.AreSequenceEqual(new[] { (1, 2) }, matches);
    }
}
