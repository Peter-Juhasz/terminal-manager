using App.Domain;
using App.Extensions.Node;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class PackageJsonScriptFileTests
{
    private static readonly PowerShellProject PowerShell = new() { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\src" };

    private static IReadOnlyList<Script> Parse(string text, Project? project = null, params string[] relatedFiles) =>
        new PackageJsonScriptFile().Parse(text, project ?? PowerShell, relatedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase));

    [TestMethod]
    public void RunsEachScriptByNpm()
    {
        var scripts = Parse("""
            {
              "name": "app",
              "scripts": {
                "build": "vite build",
                "test:unit": "vitest run"
              }
            }
            """);

        Assert.AreSequenceEqual(["build", "test:unit"], scripts.Select(static script => script.Name));
        Assert.AreSequenceEqual(["npm run build", "npm run test:unit"], scripts.Select(static script => script.Command));
        Assert.AreSequenceEqual(["vite build", "vitest run"], scripts.Select(static script => script.Description));
    }

    [TestMethod]
    public void LeavesOutScriptsRunBeforeAndAfterOthers()
    {
        var scripts = Parse("""{ "scripts": { "prebuild": "a", "build": "b", "postbuild": "c", "prepare": "d", "post": "e" } }""");

        Assert.AreSequenceEqual(["build", "prepare", "post"], scripts.Select(static script => script.Name));
    }

    [TestMethod]
    public void RunsByThePackageManagerNamed()
    {
        var scripts = Parse("""{ "packageManager": "pnpm@9.1.0", "scripts": { "build": "b" } }""", PowerShell, "yarn.lock");

        Assert.AreEqual("pnpm run build", scripts.Single().Command);
    }

    [TestMethod]
    [DataRow("pnpm-lock.yaml", "pnpm")]
    [DataRow("yarn.lock", "yarn")]
    [DataRow("bun.lockb", "bun")]
    public void RunsByThePackageManagerOfTheLockFile(string lockFile, string packageManager)
    {
        var scripts = Parse("""{ "scripts": { "build": "b" } }""", PowerShell, lockFile);

        Assert.AreEqual($"{packageManager} run build", scripts.Single().Command);
    }

    [TestMethod]
    public void QuotesNamesForTheShell()
    {
        const string text = """{ "scripts": { "build all": "b", "it's": "c" } }""";
        var cmd = new CmdProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\src" };
        var wsl = new WslProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\src" };

        Assert.AreSequenceEqual(["npm run 'build all'", "npm run 'it''s'"], Parse(text).Select(static script => script.Command));
        Assert.AreSequenceEqual(["npm run \"build all\"", "npm run \"it's\""], Parse(text, cmd).Select(static script => script.Command));
        Assert.AreSequenceEqual(["npm run 'build all'", @"npm run 'it'\''s'"], Parse(text, wsl).Select(static script => script.Command));
    }

    [TestMethod]
    public void IsNotRunByAnInterpreter()
    {
        var node = new NodeProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\src" };

        Assert.IsFalse(new PackageJsonScriptFile().IsSupportedBy(node));
        Assert.IsTrue(new PackageJsonScriptFile().IsSupportedBy(PowerShell));
    }

    [TestMethod]
    [DataRow("""{ "name": "app" }""")]
    [DataRow("""{ "scripts": {} }""")]
    [DataRow("""{ "scripts": null }""")]
    public void HasNoScriptsWithoutAny(string text) => Assert.IsEmpty(Parse(text));

    [TestMethod]
    public void TellsTheLineOfInvalidJson()
    {
        var exception = Assert.Throws<ScriptFileFormatException>(() => Parse("{\n  \"scripts\": {\n    \"build\": \n  }\n}"));

        Assert.AreEqual(4, exception.Line);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("""{ "scripts": [] }""")]
    [DataRow("""{ "scripts": { "build": 1 } }""")]
    public void RejectsInvalidScripts(string text) => Assert.Throws<ScriptFileFormatException>(() => Parse(text));
}
