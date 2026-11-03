using System.Collections.ObjectModel;
using System.Collections.Specialized;
using App.Client.WinUI3.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// What is filled in to add a project, or to edit one.
/// </summary>
public sealed partial class ProjectEditorViewModel : ObservableObject
{
    private readonly Project? _project;

    /// <summary>Starts adding a project.</summary>
    public ProjectEditorViewModel()
    {
        Kind = Kinds[0];
        EnvironmentVariables.CollectionChanged += OnEnvironmentVariablesChanged;
    }

    /// <summary>Starts editing <paramref name="project"/>, whose kind can't be changed.</summary>
    public ProjectEditorViewModel(Project project)
        : this()
    {
        ArgumentNullException.ThrowIfNull(project);

        _project = project;
        Kind = new ProjectKind(project.TerminalDisplayName, (_, _, _) => project);
        Kinds = (ProjectKind[])[Kind];
        DisplayName = project.DisplayName;
        WorkingFolder = project.WorkingFolder;
        LoadEnvFile = project is LocalProject { LoadEnvFile: true };
        PersistHistory = project.PersistHistory;
        foreach (var (name, value) in project.EnvironmentVariables ?? ReadOnlyDictionary<string, string>.Empty)
        {
            EnvironmentVariables.Add(new(Remove) { Name = name, Value = value });
        }

        if (project is PowerShellProject powerShell)
        {
            PrependScript = powerShell.PrependScript ?? string.Empty;
            AppendScript = powerShell.AppendScript ?? string.Empty;
        }

        if (project is DotNetFileProject dotNetFile)
        {
            PrependScript = dotNetFile.PrependScript ?? string.Empty;
            AppendScript = dotNetFile.AppendScript ?? string.Empty;
        }

        if (project is DotNetRunProject dotNetRun)
        {
            Arguments = dotNetRun.RunArguments ?? string.Empty;
        }

        if (project is InterpreterProject interpreter)
        {
            Arguments = interpreter.Arguments ?? string.Empty;
            PrependScript = interpreter.PrependScript ?? string.Empty;
            AppendScript = interpreter.AppendScript ?? string.Empty;
        }

        if (project is AzureVmProject azureVm)
        {
            Subscription = azureVm.Subscription ?? string.Empty;
            ResourceGroup = azureVm.ResourceGroup;
            VmName = azureVm.VmName;
            PrependScript = azureVm.PrependScript ?? string.Empty;
            AppendScript = azureVm.AppendScript ?? string.Empty;
        }
    }

    public bool IsNew => _project is null;

    public string Title => IsNew ? "New project" : "Edit project";

    public string PrimaryButtonText => IsNew ? "Add" : "Save";

    public IReadOnlyList<ProjectKind> Kinds { get; } = ProjectKind.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrependAndAppend), nameof(ScriptsDescription), nameof(PrependScriptPlaceholder), nameof(AppendScriptPlaceholder))]
    [NotifyPropertyChangedFor(nameof(HasArguments), nameof(ArgumentsHeader), nameof(ArgumentsPlaceholder), nameof(ArgumentsDescription))]
    [NotifyPropertyChangedFor(nameof(IsAzureVm), nameof(IsWorkingFolderLocal), nameof(WorkingFolderHeader), nameof(WorkingFolderPlaceholder), nameof(IsWorkingFolderMissing))]
    [NotifyPropertyChangedFor(nameof(EnvironmentVariablesDescription), nameof(IsValid))]
    public partial ProjectKind Kind { get; set; }

    // the kind is told by what it creates, as that of a project being edited creates the project itself.
    private Project Sample => Kind.Create(Guid.Empty, string.Empty, string.Empty);

    public bool HasPrependAndAppend => Sample is PowerShellProject or DotNetFileProject or InterpreterProject or AzureVmProject;

    /// <summary>Whether commands are run on an Azure virtual machine, which is told by its subscription, resource group and name.</summary>
    public bool IsAzureVm => Sample is AzureVmProject;

    /// <summary>Whether the working folder is on this machine, so it can be browsed for, and is checked to exist; elsewhere, it is only changed to before each command.</summary>
    public bool IsWorkingFolderLocal => Sample is LocalProject;

    public string WorkingFolderHeader => IsWorkingFolderLocal ? "Working folder" : "Working folder on the machine";

    public string WorkingFolderPlaceholder => Sample switch
    {
        AzureVmShellProject => "/home/azureuser/my-project",
        _ => @"C:\src\my-project",
    };

    public string EnvironmentVariablesDescription => IsWorkingFolderLocal
        ? "Commands are run with these, in addition to the variables of the app. Use %NAME% in a value to include another variable."
        : "These are set on the machine before each command. Use %NAME% in a value to include a variable of this machine.";

    /// <summary>The name or id of the subscription the machine is in, or empty for the default subscription of the Azure CLI, which only projects run on an Azure virtual machine have.</summary>
    [ObservableProperty]
    public partial string Subscription { get; set; } = string.Empty;

    /// <summary>The resource group the machine is in, which only projects run on an Azure virtual machine have.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string ResourceGroup { get; set; } = string.Empty;

    /// <summary>The name of the machine, which only projects run on an Azure virtual machine have.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string VmName { get; set; } = string.Empty;

    public string ScriptsDescription => Sample switch
    {
        DotNetFileProject => "Put before and after each command, in the same file, so what the prepend script declares, such as #: and using directives, applies to commands.",
        PythonProject => "Put before and after each command, in the same script, so what the prepend script defines can be used by commands. Neither may be indented.",
        InterpreterProject => "Put before and after each command, in the same script, so what the prepend script defines can be used by commands.",
        AzureVmProject => "Run on the machine before and after each command, in the same script, so what the prepend script defines can be used by commands.",
        _ => "Run before and after each command, in the same scope, so what the prepend script defines can be used by commands, and is offered by completion.",
    };

    public string PrependScriptPlaceholder => Sample switch
    {
        DotNetFileProject => "#:package Humanizer@2.14.1\nusing Humanizer;",
        PythonProject => "import json",
        NodeProject => "const fs = require('node:fs');",
        AzureVmShellProject => ". ./env.sh",
        _ => "Import-Module ./build.psm1",
    };

    public string AppendScriptPlaceholder => Sample switch
    {
        DotNetFileProject => "Console.WriteLine(\"Done.\");",
        PythonProject => "print(\"Done.\")",
        NodeProject => "console.log('Done.');",
        AzureVmShellProject => "echo \"Exit code: $?\"",
        _ => "Write-Host \"Exit code: $LASTEXITCODE\"",
    };

    public bool HasArguments => Sample is DotNetRunProject or InterpreterProject;

    public string ArgumentsHeader => Sample switch
    {
        PythonProject => "Options of python",
        NodeProject => "Options of node",
        _ => "Arguments of dotnet run",
    };

    public string ArgumentsPlaceholder => Sample switch
    {
        PythonProject => "-X dev",
        NodeProject => "--input-type=module",
        DotNetFileProject => "-c Release -- arg1",
        _ => "--project src/MyApp -c Release",
    };

    /// <summary>How each command is run with <see cref="Arguments"/>.</summary>
    public string ArgumentsDescription => Sample switch
    {
        DotNetFileProject => "Each command is run as the code of a file-based app, as in dotnet run - [arguments]. Pass arguments to the app after --.",
        PythonProject => "Each command is run as a script, as in python [options] -.",
        NodeProject => "Each command is run as a script, as in node [options] -.",
        _ => "Each command is passed to the app after these, as in dotnet run [arguments] -- [command].",
    };

    /// <summary>The arguments of <c>dotnet run</c> itself, or the options of the interpreter, which only projects run by one of them have.</summary>
    [ObservableProperty]
    public partial string Arguments { get; set; } = string.Empty;

    /// <summary>The script run before each command, which only projects run by PowerShell, or whose commands are code, have.</summary>
    [ObservableProperty]
    public partial string PrependScript { get; set; } = string.Empty;

    /// <summary>The script run after each command, which only projects run by PowerShell, or whose commands are code, have.</summary>
    [ObservableProperty]
    public partial string AppendScript { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsWorkingFolderMissing))]
    public partial string WorkingFolder { get; set; } = string.Empty;

    public bool IsWorkingFolderMissing => IsWorkingFolderLocal && !string.IsNullOrWhiteSpace(WorkingFolder) && !Directory.Exists(ExpandedWorkingFolder);

    // the environment variables in it are expanded when it is used, so it is checked the same way.
    private string ExpandedWorkingFolder => Environment.ExpandEnvironmentVariables(WorkingFolder.Trim());

    /// <summary>The environment variables commands are run with, in the order they were added.</summary>
    public ObservableCollection<EnvironmentVariableEditorViewModel> EnvironmentVariables { get; } = [];

    /// <summary>Whether commands are also run with the variables in the .env file in the working folder.</summary>
    [ObservableProperty]
    public partial bool LoadEnvFile { get; set; }

    /// <summary>Whether the commands that were run, with what they wrote, are saved, and shown again after the app is started.</summary>
    [ObservableProperty]
    public partial bool PersistHistory { get; set; }

    /// <summary>Why the environment variables can't be saved, or <see langword="null"/> when they can.</summary>
    public string? EnvironmentVariablesError
    {
        get
        {
            var names = FilledEnvironmentVariables.Select(static variable => variable.Name.Trim()).ToList();
            if (names.Any(string.IsNullOrEmpty))
            {
                return "Each variable needs a name.";
            }

            // the system can't tell a name with one apart from its value
            if (names.Any(static name => name.Contains('=')))
            {
                return "A name can't contain '='.";
            }

            // names of environment variables are case insensitive on Windows
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            {
                return "Each variable needs a name of its own.";
            }

            return null;
        }
    }

    public bool HasEnvironmentVariablesError => EnvironmentVariablesError is not null;

    public bool IsValid => !string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(WorkingFolder) && !IsWorkingFolderMissing && !HasEnvironmentVariablesError
        && (!IsAzureVm || (!string.IsNullOrWhiteSpace(ResourceGroup) && !string.IsNullOrWhiteSpace(VmName)));

    private IEnumerable<EnvironmentVariableEditorViewModel> FilledEnvironmentVariables => EnvironmentVariables.Where(static variable => !variable.IsEmpty);

    [RelayCommand]
    private void AddEnvironmentVariable() => EnvironmentVariables.Add(new(Remove));

    private void Remove(EnvironmentVariableEditorViewModel variable) => EnvironmentVariables.Remove(variable);

    private void OnEnvironmentVariablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (EnvironmentVariableEditorViewModel variable in e.OldItems ?? Array.Empty<object>())
        {
            variable.PropertyChanged -= OnEnvironmentVariableChanged;
        }

        foreach (EnvironmentVariableEditorViewModel variable in e.NewItems ?? Array.Empty<object>())
        {
            variable.PropertyChanged += OnEnvironmentVariableChanged;
        }

        OnEnvironmentVariableChanged(sender, e);
    }

    private void OnEnvironmentVariableChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EnvironmentVariablesError));
        OnPropertyChanged(nameof(HasEnvironmentVariablesError));
        OnPropertyChanged(nameof(IsValid));
    }

    public Project Build()
    {
        // what isn't filled in is left unset, so it isn't saved
        IReadOnlyDictionary<string, string>? environmentVariables = FilledEnvironmentVariables.ToDictionary(static variable => variable.Name.Trim(), static variable => variable.Value, StringComparer.OrdinalIgnoreCase) switch
        {
            { Count: 0 } => null,
            var variables => variables,
        };
        var built = _project switch
        {
            null => Kind.Create(Guid.NewGuid(), DisplayName.Trim(), WorkingFolder.Trim()) with { EnvironmentVariables = environmentVariables, PersistHistory = PersistHistory },
            { } project => project with { DisplayName = DisplayName.Trim(), WorkingFolder = WorkingFolder.Trim(), EnvironmentVariables = environmentVariables, PersistHistory = PersistHistory },
        };

        if (built is LocalProject local)
        {
            built = local with { LoadEnvFile = LoadEnvFile };
        }

        // a script of only blank lines would still move what is typed down in the script that is run
        return built switch
        {
            PowerShellProject powerShell => powerShell with { PrependScript = NullIfEmpty(PrependScript.TrimEnd()), AppendScript = NullIfEmpty(AppendScript.TrimEnd()) },
            DotNetFileProject dotNetFile => dotNetFile with
            {
                RunArguments = NullIfEmpty(Arguments.Trim()),
                PrependScript = NullIfEmpty(PrependScript.TrimEnd()),
                AppendScript = NullIfEmpty(AppendScript.TrimEnd()),
            },
            DotNetRunProject dotNetRun => dotNetRun with { RunArguments = NullIfEmpty(Arguments.Trim()) },
            InterpreterProject interpreter => interpreter with
            {
                Arguments = NullIfEmpty(Arguments.Trim()),
                PrependScript = NullIfEmpty(PrependScript.TrimEnd()),
                AppendScript = NullIfEmpty(AppendScript.TrimEnd()),
            },
            AzureVmProject azureVm => azureVm with
            {
                Subscription = NullIfEmpty(Subscription.Trim()),
                ResourceGroup = ResourceGroup.Trim(),
                VmName = VmName.Trim(),
                PrependScript = NullIfEmpty(PrependScript.TrimEnd()),
                AppendScript = NullIfEmpty(AppendScript.TrimEnd()),
            },
            _ => built,
        };
    }

    private static string? NullIfEmpty(string value) => value switch
    {
        "" => null,
        _ => value,
    };
}
