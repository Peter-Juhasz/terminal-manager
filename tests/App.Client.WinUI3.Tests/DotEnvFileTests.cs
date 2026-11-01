using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class DotEnvFileTests
{
    [TestMethod]
    public void ParsesNamesAndValues()
    {
        var variables = DotEnvFile.Parse("""
            # a comment
            A=1

              B = two words
            export C=3
            NO_SEPARATOR
            =no name
            EMPTY=
            """);

        Assert.HasCount(4, variables);
        Assert.AreEqual("1", variables["A"]);
        Assert.AreEqual("two words", variables["B"]);
        Assert.AreEqual("3", variables["C"]);
        Assert.AreEqual(string.Empty, variables["EMPTY"]);
    }

    [TestMethod]
    public void UnquotesValues()
    {
        var variables = DotEnvFile.Parse("""
            DOUBLE="a # b\n\"c\"\\"
            SINGLE='a\n # b'
            UNQUOTED=a # comment
            EQUALS=a=b
            """);

        Assert.AreEqual("a # b\n\"c\"\\", variables["DOUBLE"]);
        Assert.AreEqual(@"a\n # b", variables["SINGLE"]);
        Assert.AreEqual("a", variables["UNQUOTED"]);
        Assert.AreEqual("a=b", variables["EQUALS"]);
    }

    [TestMethod]
    public void LaterValueReplacesEarlierByNameOfAnyCase()
    {
        var variables = DotEnvFile.Parse("A=1\na=2");

        Assert.HasCount(1, variables);
        Assert.AreEqual("2", variables["A"]);
    }

    [TestMethod]
    public void MissingFileHasNoVariables()
    {
        Assert.IsEmpty(DotEnvFile.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), DotEnvFile.FileName)));
    }

    [TestMethod]
    public void ProjectVariablesReplaceThoseInFile()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, DotEnvFile.FileName), "FROM_FILE=1\nSHARED=file");
            var project = new PowerShellProject
            {
                Id = Guid.NewGuid(),
                DisplayName = "A",
                WorkingFolder = folder.FullName,
                EnvironmentVariables = new Dictionary<string, string> { ["shared"] = "project" },
            };

            Assert.HasCount(1, project.ResolveEnvironmentVariables());

            var variables = (project with { LoadEnvFile = true }).ResolveEnvironmentVariables();

            Assert.HasCount(2, variables);
            Assert.AreEqual("1", variables["FROM_FILE"]);
            Assert.AreEqual("project", variables["SHARED"]);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
