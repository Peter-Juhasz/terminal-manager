using System.Collections.ObjectModel;
using System.Collections.Specialized;
using App.Domain;
using App.Services;
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
        Kind = ProjectKind.All[0];
        EnvironmentVariables.CollectionChanged += OnEnvironmentVariablesChanged;
    }

    /// <summary>Starts editing <paramref name="project"/>, whose kind can't be changed.</summary>
    public ProjectEditorViewModel(Project project)
        : this()
    {
        ArgumentNullException.ThrowIfNull(project);

        _project = project;
        Kind = new ProjectKind(DisplayName: project.TerminalDisplayName, Description: string.Empty, Create: (_, _, _) => project);
        DisplayName = project.DisplayName;
        WorkingFolder = project.WorkingFolder;
        LoadEnvFile = project is LocalProject { LoadEnvFile: true };
        LoadScriptFiles = project is LocalProject { IgnoreScriptFiles: false };
        PersistHistory = project.PersistHistory;
        CollapseSucceededOutput = project.CollapseSucceededOutput;
        LineNumbersIndex = project.ShowLineNumbers switch
        {
            true => 1,
            false => 2,
            null => 0,
        };
        PrependScript = project.PrependScript ?? string.Empty;
        AppendScript = project.AppendScript ?? string.Empty;
        foreach (var (name, value) in project.EnvironmentVariables ?? ReadOnlyDictionary<string, string>.Empty)
        {
            EnvironmentVariables.Add(new(Remove) { Name = name, Value = value });
        }

        if (project is DotNetRunProject dotNetRun)
        {
            Arguments = dotNetRun.RunArguments ?? string.Empty;
        }

        if (project is InterpreterProject interpreter)
        {
            Arguments = interpreter.Arguments ?? string.Empty;
        }

        if (project is AzureVmProject azureVm)
        {
            Subscription = azureVm.Subscription ?? string.Empty;
            ResourceGroup = azureVm.ResourceGroup;
            VmName = azureVm.VmName;
        }

        if (project is DockerProject docker)
        {
            Context = docker.Context ?? string.Empty;
            Container = docker.Container;
            User = docker.User ?? string.Empty;
        }

        if (project is WslProject wsl)
        {
            Distribution = wsl.Distribution ?? string.Empty;
            User = wsl.User ?? string.Empty;
        }

        if (project is PowerShellRemotingProject remoting)
        {
            ComputerName = remoting.ComputerName;
        }

        if (project is TelnetProject telnet)
        {
            Host = telnet.Host;
            Port = telnet.Port ?? double.NaN;
            User = telnet.User ?? string.Empty;
        }

        if (project is KubectlProject kubectl)
        {
            Context = kubectl.Context ?? string.Empty;
            Namespace = kubectl.Namespace ?? string.Empty;
            Pod = kubectl.Pod;
            Container = kubectl.Container ?? string.Empty;
        }
    }

    public bool IsNew => _project is null;

    public string Title => IsNew ? "New project" : "Edit project";

    public string PrimaryButtonText => IsNew ? "Add" : "Save";

    /// <summary>The kind of the project, which is picked before the dialog is shown when one is added, and can't be changed in it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrependAndAppend), nameof(ScriptsDescription), nameof(PrependScriptPlaceholder), nameof(AppendScriptPlaceholder))]
    [NotifyPropertyChangedFor(nameof(HasArguments), nameof(ArgumentsHeader), nameof(ArgumentsPlaceholder), nameof(ArgumentsDescription))]
    [NotifyPropertyChangedFor(nameof(IsAzureVm), nameof(IsWorkingFolderLocal), nameof(WorkingFolderHeader), nameof(WorkingFolderPlaceholder), nameof(IsWorkingFolderMissing))]
    [NotifyPropertyChangedFor(nameof(IsDocker), nameof(IsKubectl), nameof(ContainerDescription), nameof(IsPowerShellRemoting), nameof(IsWsl), nameof(IsTelnet))]
    [NotifyPropertyChangedFor(nameof(EnvironmentVariablesDescription), nameof(IsValid))]
    public partial ProjectKind Kind { get; set; }

    private Project Sample => Kind.Sample;

    public bool HasPrependAndAppend => Sample.HasPrependAndAppend;

    /// <summary>Whether commands are run on an Azure virtual machine, which is told by its subscription, resource group and name.</summary>
    public bool IsAzureVm => Sample is AzureVmProject;

    /// <summary>Whether commands are run in a Docker container, which is told by its context, name and the user they are run as.</summary>
    public bool IsDocker => Sample is DockerProject;

    /// <summary>Whether commands are run in a container of a Kubernetes pod, which is told by its context, namespace, pod and container.</summary>
    public bool IsKubectl => Sample is KubectlProject;

    /// <summary>Whether commands are run in a distribution of the Windows Subsystem for Linux, which is told by its name and the user they are run as.</summary>
    public bool IsWsl => Sample is WslProject;

    /// <summary>The distribution of the Windows Subsystem for Linux commands are run in, or empty for the default distribution, which only projects run by WSL have.</summary>
    [ObservableProperty]
    public partial string Distribution { get; set; } = string.Empty;

    /// <summary>Whether commands are run on another computer by PowerShell remoting, which is told by its name.</summary>
    public bool IsPowerShellRemoting => Sample is PowerShellRemotingProject;

    /// <summary>The name of the computer commands are run on by PowerShell remoting, which only projects run by it have, and which they need.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string ComputerName { get; set; } = string.Empty;

    /// <summary>Whether commands are run on another computer over Telnet, which is told by its host, port, and the user that is logged in as.</summary>
    public bool IsTelnet => Sample is TelnetProject;

    /// <summary>The name or address of the computer commands are run on over Telnet, which only projects run by it have, and which they need.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Host { get; set; } = string.Empty;

    /// <summary>The port Telnet is served on, or <see cref="double.NaN"/> when it isn't filled in, for the default port, as a number box tells it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial double Port { get; set; } = double.NaN;

    /// <summary>
    /// The password of <see cref="User"/>, which is saved in the Credential Manager of Windows, rather than in the project, once the project is;
    /// empty to keep the one saved before, if there is one.
    /// </summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    private bool IsPortValid => double.IsNaN(Port) || Port is >= 1 and <= 65535;

    /// <summary>Whether the working folder is on this machine, so it can be browsed for, and is checked to exist; elsewhere, it is only changed to before each command.</summary>
    public bool IsWorkingFolderLocal => Sample is LocalProject;

    public string WorkingFolderHeader => Sample switch
    {
        WslProject => "Working folder, of Windows or Linux",
        LocalProject => "Working folder",
        ContainerProject => "Working folder in the container",
        _ => "Working folder on the machine",
    };

    public string WorkingFolderPlaceholder => Sample switch
    {
        AzureVmShellProject => "/home/azureuser/my-project",
        TelnetProject => "~/my-project",
        WslProject => @"C:\src\my-project or ~/src/my-project",
        ContainerProject => "/app",
        _ => @"C:\src\my-project",
    };

    public string EnvironmentVariablesDescription => Sample switch
    {
        WslProject => "These are set in Linux before each command, after the profile is read. Use %NAME% in a value to include a variable of this machine.",
        LocalProject => "Commands are run with these, in addition to the variables of the app. Use %NAME% in a value to include another variable.",
        ContainerProject => "These are set in the container before each command. Use %NAME% in a value to include a variable of this machine.",
        _ => "These are set on the machine before each command. Use %NAME% in a value to include a variable of this machine.",
    };

    /// <summary>How each command is run in the container.</summary>
    public string ContainerDescription => Sample switch
    {
        DockerShellProject => "Each command is run by docker exec, as a script of sh, in the working folder in the running container.",
        DockerPowerShellProject => "Each command is run by docker exec, as a script of pwsh, in the working folder in the running container.",
        KubectlShellProject => "Each command is run by kubectl exec, as a script of sh, in the working folder in the container.",
        KubectlPowerShellProject => "Each command is run by kubectl exec, as a script of pwsh, in the working folder in the container.",
        _ => string.Empty,
    };

    /// <summary>The Docker or kubeconfig context the container is in, or empty for the current one, which only projects run in a container have.</summary>
    [ObservableProperty]
    public partial string Context { get; set; } = string.Empty;

    /// <summary>
    /// The name or id of the Docker container, which only projects run in one have, and which they need; or the container in the Kubernetes pod,
    /// or empty for its default container.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Container { get; set; } = string.Empty;

    /// <summary>
    /// The user commands are run as in the Docker container, or in the WSL distribution, or empty for that of the container, or the default user of the distribution;
    /// or the user that is logged in as over Telnet, or empty when the computer doesn't ask for one. Only projects run in one of them have it.
    /// </summary>
    [ObservableProperty]
    public partial string User { get; set; } = string.Empty;

    /// <summary>The namespace the Kubernetes pod is in, or empty for that of the context, which only projects run in one have.</summary>
    [ObservableProperty]
    public partial string Namespace { get; set; } = string.Empty;

    /// <summary>The Kubernetes pod, or a resource kubectl picks one of, which only projects run in one have.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Pod { get; set; } = string.Empty;

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
        AzureVmProject or PowerShellRemotingProject or TelnetProject => "Run on the machine before and after each command, in the same script, so what the prepend script defines can be used by commands.",
        ContainerProject => "Run in the container before and after each command, in the same script, so what the prepend script defines can be used by commands.",
        WslProject => "Run in Linux before and after each command, in the same script, so what the prepend script defines can be used by commands.",
        _ => "Run before and after each command, in the same scope, so what the prepend script defines can be used by commands, and is offered by completion.",
    };

    public string PrependScriptPlaceholder => Sample switch
    {
        DotNetFileProject => "#:package Humanizer@2.14.1\nusing Humanizer;",
        PythonProject => "import json",
        NodeProject => "const fs = require('node:fs');",
        AzureVmShellProject or DockerShellProject or KubectlShellProject or WslProject or TelnetProject => ". ./env.sh",
        _ => "Import-Module ./build.psm1",
    };

    public string AppendScriptPlaceholder => Sample switch
    {
        DotNetFileProject => "Console.WriteLine(\"Done.\");",
        PythonProject => "print(\"Done.\")",
        NodeProject => "console.log('Done.');",
        AzureVmShellProject or DockerShellProject or KubectlShellProject or WslProject or TelnetProject => "echo \"Exit code: $?\"",
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

    // a folder of Linux is only changed to when commands are run, as the distribution may not even be running yet
    public bool IsWorkingFolderMissing => IsWorkingFolderLocal && !string.IsNullOrWhiteSpace(WorkingFolder) && !(IsWsl && WslProject.IsLinuxFolder(WorkingFolder)) && !Directory.Exists(ExpandedWorkingFolder);

    // the environment variables in it are expanded when it is used, so it is checked the same way.
    private string ExpandedWorkingFolder => Environment.ExpandEnvironmentVariables(WorkingFolder.Trim());

    /// <summary>The environment variables commands are run with, in the order they were added.</summary>
    public ObservableCollection<EnvironmentVariableEditorViewModel> EnvironmentVariables { get; } = [];

    /// <summary>Whether commands are also run with the variables in the .env file in the working folder.</summary>
    [ObservableProperty]
    public partial bool LoadEnvFile { get; set; }

    /// <summary>Whether the scripts in well known files in the working folder, such as .terminal-manager.yml and package.json, are shown too.</summary>
    [ObservableProperty]
    public partial bool LoadScriptFiles { get; set; } = true;

    /// <summary>Whether the commands that were run, with what they wrote, are saved, and shown again after the app is started.</summary>
    [ObservableProperty]
    public partial bool PersistHistory { get; set; }

    /// <summary>Whether the output of a command is collapsed, and is only expanded once it failed.</summary>
    [ObservableProperty]
    public partial bool CollapseSucceededOutput { get; set; }

    /// <summary>Whether the lines of the output are numbered, as the index of the choice: following the setting of the app, shown, or hidden.</summary>
    [ObservableProperty]
    public partial int LineNumbersIndex { get; set; }

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
        && (!IsAzureVm || (!string.IsNullOrWhiteSpace(ResourceGroup) && !string.IsNullOrWhiteSpace(VmName)))
        && (!IsDocker || !string.IsNullOrWhiteSpace(Container))
        && (!IsKubectl || !string.IsNullOrWhiteSpace(Pod))
        && (!IsPowerShellRemoting || !string.IsNullOrWhiteSpace(ComputerName))
        && (!IsTelnet || (!string.IsNullOrWhiteSpace(Host) && IsPortValid));

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
            null => Kind.Create(Guid.NewGuid(), DisplayName.Trim(), WorkingFolder.Trim()),
            { } project => project with { DisplayName = DisplayName.Trim(), WorkingFolder = WorkingFolder.Trim() },
        };

        // a script of only blank lines would still move what is typed down in the script that is run;
        // the scripts are left empty for kinds that have none, as they aren't shown for them.
        built = built with
        {
            EnvironmentVariables = environmentVariables,
            PersistHistory = PersistHistory,
            CollapseSucceededOutput = CollapseSucceededOutput,
            ShowLineNumbers = LineNumbersIndex switch
            {
                1 => true,
                2 => false,
                _ => null,
            },
            PrependScript = NullIfEmpty(PrependScript.TrimEnd()),
            AppendScript = NullIfEmpty(AppendScript.TrimEnd()),
        };

        if (built is LocalProject local)
        {
            built = local with { LoadEnvFile = LoadEnvFile, IgnoreScriptFiles = !LoadScriptFiles };
        }

        return built switch
        {
            DotNetRunProject dotNetRun => dotNetRun with { RunArguments = NullIfEmpty(Arguments.Trim()) },
            InterpreterProject interpreter => interpreter with { Arguments = NullIfEmpty(Arguments.Trim()) },
            AzureVmProject azureVm => azureVm with
            {
                Subscription = NullIfEmpty(Subscription.Trim()),
                ResourceGroup = ResourceGroup.Trim(),
                VmName = VmName.Trim(),
            },
            DockerProject docker => docker with
            {
                Context = NullIfEmpty(Context.Trim()),
                Container = Container.Trim(),
                User = NullIfEmpty(User.Trim()),
            },
            WslProject wsl => wsl with
            {
                Distribution = NullIfEmpty(Distribution.Trim()),
                User = NullIfEmpty(User.Trim()),
            },
            PowerShellRemotingProject remoting => remoting with { ComputerName = ComputerName.Trim() },
            TelnetProject telnet => telnet with
            {
                Host = Host.Trim(),
                Port = double.IsNaN(Port) || (int)Port is TelnetProject.DefaultPort ? null : (int)Port,
                User = NullIfEmpty(User.Trim()),
            },
            KubectlProject kubectl => kubectl with
            {
                Context = NullIfEmpty(Context.Trim()),
                Namespace = NullIfEmpty(Namespace.Trim()),
                Pod = Pod.Trim(),
                Container = NullIfEmpty(Container.Trim()),
            },
            _ => built,
        };
    }

    /// <summary>
    /// Saves <see cref="Password"/> to <paramref name="credentials"/>, for the user of <paramref name="project"/>, which <see cref="Build"/> returned,
    /// when one was filled in, and the project logs in as a user.
    /// </summary>
    public void SavePassword(Project project, ICredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(credentials);

        if (Password is not { Length: > 0 } || project is not TelnetProject { User: { Length: > 0 } user } telnet)
        {
            return;
        }

        credentials.Write(telnet.CredentialTarget, user, Password);
    }

    private static string? NullIfEmpty(string value) => value switch
    {
        "" => null,
        _ => value,
    };
}
