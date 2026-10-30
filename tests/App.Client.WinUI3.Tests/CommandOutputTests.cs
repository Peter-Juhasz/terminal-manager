using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class CommandOutputTests
{
    private const OutputStream Out = OutputStream.StandardOutput;
    private const OutputStream Error = OutputStream.StandardError;

    [TestMethod]
    public void AppendSkipsEmptyChunks()
    {
        var output = new CommandOutput();
        var appended = 0;
        output.Appended += (_, _) => appended++;

        output.Append([new(Out, "a"), new(Error, string.Empty), default, new(Error, "b")]);

        Assert.AreSequenceEqual(new OutputChunk[] { new(Out, "a"), new(Error, "b") }, output.Chunks);
        Assert.AreEqual(1, appended);
        Assert.IsFalse(output.IsEmpty);
    }

    [TestMethod]
    public void AppendOfNothingDoesNotRaiseAppended()
    {
        var output = new CommandOutput();
        var appended = 0;
        output.Appended += (_, _) => appended++;

        output.Append([]);
        output.Append([new(Out, string.Empty)]);

        Assert.AreEqual(0, appended);
        Assert.IsTrue(output.IsEmpty);
    }

    [TestMethod]
    public void ToStringJoinsAllChunks()
    {
        var output = new CommandOutput();
        output.Append([new(Out, "a"), new(Error, "b")]);
        output.Append([new(Out, "c")]);

        Assert.AreEqual("abc", output.ToString());
    }

    [TestMethod]
    public void ToStringOfNothingIsEmpty() => Assert.AreEqual(string.Empty, new CommandOutput().ToString());
}
