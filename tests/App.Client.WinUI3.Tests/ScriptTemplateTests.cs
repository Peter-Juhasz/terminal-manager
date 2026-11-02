using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ScriptTemplateTests
{
    [TestMethod]
    public void FindsEachVariableOnce()
    {
        var variables = ScriptTemplate.GetVariables("git checkout {{ branch }} && git push {{remote}} {{  branch  }} {{ Branch }} {{ }} {{ two words }}");

        Assert.AreSequenceEqual(["branch", "remote", "Branch", "two words"], variables.Select(variable => variable.Name));
    }

    [TestMethod]
    public void TakesFirstDefaultValue()
    {
        var variables = ScriptTemplate.GetVariables("{{ a }} {{ a = 1 }} {{a=2}} {{ b= x = y }} {{ c = }} {{ d }} {{ = e }}");

        Assert.AreSequenceEqual(
            [
                new TemplateVariable(Name: "a", DefaultValue: "1"),
                new TemplateVariable(Name: "b", DefaultValue: "x = y"),
                new TemplateVariable(Name: "c", DefaultValue: ""),
                new TemplateVariable(Name: "d", DefaultValue: null),
            ],
            variables);
    }

    [TestMethod]
    public void ExpandsOccurrencesWithDefaultValue()
    {
        var command = ScriptTemplate.Expand("echo {{ a = 1 }} {{ a }} {{ b = 2 }}", new Dictionary<string, string>
        {
            ["a"] = "3",
        });

        Assert.AreEqual("echo 3 3 {{ b = 2 }}", command);
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
