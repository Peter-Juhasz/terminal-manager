using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ScriptTemplateTests
{
    [TestMethod]
    public void FindsEachVariableOnce()
    {
        var variables = ScriptTemplate.GetVariables("git checkout {{ branch }} && git push {{remote}} {{  branch  }} {{ Branch }} {{ }} {{ two words }}");

        Assert.AreSequenceEqual(["branch", "remote", "Branch", "two words"], variables);
    }

    [TestMethod]
    public void FindsNoneWithoutVariables()
    {
        Assert.IsEmpty(ScriptTemplate.GetVariables("echo { a } {{}} $env:PATH"));
    }

    [TestMethod]
    public void ExpandsEachOccurrence()
    {
        var command = ScriptTemplate.Expand("echo {{ a }} {{a}} {{ b }} {{ c }}", new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "{{ a }}",
        });

        Assert.AreEqual("echo 1 1 {{ a }} {{ c }}", command);
    }
}
