using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class TerminalManagerScriptFileTests
{
    private static readonly HashSet<string> NoRelatedFiles = [];

    private static readonly PowerShellProject PowerShell = new() { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\src" };

    private static readonly CmdProject Cmd = new() { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\src" };

    private static IReadOnlyList<Script> Parse(string text, Project? project = null) =>
        new TerminalManagerScriptFile().Parse(text, project ?? PowerShell, NoRelatedFiles);

    [TestMethod]
    public void ReadsCommandsInOrder()
    {
        var scripts = Parse("""
            scripts:
            - name: Build
              command: dotnet build
            - name: Deploy {{ environment }}
              description: Deploys to the given environment
              command: |
                ./deploy.ps1 -Environment {{ environment }}
            - name: Test
              command: dotnet test
            """);

        Assert.AreSequenceEqual(["Build", "Deploy {{ environment }}", "Test"], scripts.Select(static script => script.Name));
        Assert.AreSequenceEqual(["dotnet build", "./deploy.ps1 -Environment {{ environment }}", "dotnet test"], scripts.Select(static script => script.Command));
        Assert.AreSequenceEqual([null, "Deploys to the given environment", null], scripts.Select(static script => script.Description));
        Assert.AreSequenceEqual([false, true, false], scripts.Select(static script => script.IsTemplate));
    }

    [TestMethod]
    public void GivesTheSameIdEachTime()
    {
        const string text = "scripts:\n- name: Build\n  command: dotnet build\n- name: Test\n  command: dotnet test\n";

        var first = Parse(text);
        var second = Parse(text.Replace("dotnet test", "dotnet test --no-build", StringComparison.Ordinal));

        Assert.AreSequenceEqual(first.Select(static script => script.Id), second.Select(static script => script.Id));
        Assert.AreNotEqual(first[0].Id, first[1].Id);
    }

    [TestMethod]
    public void LeavesOutScriptsOfOtherLanguages()
    {
        const string text = """
            scripts:
            - name: Any
              command: echo any
            - name: PowerShell
              language: pwsh
              command: Write-Output a
            - name: Both
              language: [cmd, powershell]
              command: echo both
            - name: Cmd
              language: cmd
              command: echo cmd
            """;

        Assert.AreSequenceEqual(["Any", "PowerShell", "Both"], Parse(text, PowerShell).Select(static script => script.Name));
        Assert.AreSequenceEqual(["Any", "Both", "Cmd"], Parse(text, Cmd).Select(static script => script.Name));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("# nothing yet\n")]
    [DataRow("scripts:\n")]
    [DataRow("version: 2\n")]
    public void HasNoScriptsWithoutAny(string text) => Assert.IsEmpty(Parse(text));

    [TestMethod]
    public void TellsTheLineOfInvalidYaml()
    {
        var exception = Assert.Throws<ScriptFileFormatException>(() => Parse("scripts:\n- name: Build\n  command: \"dotnet build\n"));

        Assert.IsNotNull(exception.Line);
    }

    [TestMethod]
    [DataRow("- Build\n", DisplayName = "not a mapping")]
    [DataRow("scripts:\n  Build: dotnet build\n", DisplayName = "scripts not a list")]
    [DataRow("scripts:\n- dotnet build\n", DisplayName = "script not a mapping")]
    [DataRow("scripts:\n- name: Build\n  description: no command\n", DisplayName = "no command")]
    [DataRow("scripts:\n- name: Build\n  command: \"  \"\n", DisplayName = "blank command")]
    [DataRow("scripts:\n- name: Build\n  command: [a, b]\n", DisplayName = "command not text")]
    [DataRow("scripts:\n- name: Build\n  language: fortran\n  command: make\n", DisplayName = "unknown language")]
    [DataRow("scripts:\n- name: Build\n  command: a\n- name: Build\n  command: b\n", DisplayName = "written twice")]
    [DataRow("scripts:\n- command: a\n", DisplayName = "no name")]
    [DataRow("scripts:\n- name: \"\"\n  command: a\n", DisplayName = "blank name")]
    [DataRow("scripts:\n- name: [a]\n  command: a\n", DisplayName = "name not text")]
    public void RejectsInvalidScripts(string text) => Assert.Throws<ScriptFileFormatException>(() => Parse(text));
}
