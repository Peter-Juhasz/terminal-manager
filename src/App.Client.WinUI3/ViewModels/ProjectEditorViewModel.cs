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
        LoadEnvFile = project.LoadEnvFile;
        foreach (var (name, value) in project.EnvironmentVariables)
        {
            EnvironmentVariables.Add(new(Remove) { Name = name, Value = value });
        }

        if (project is PowerShellProject powerShell)
        {
            PrependScript = powerShell.PrependScript;
            AppendScript = powerShell.AppendScript;
        }

        if (project is DotNetRunProject dotNetRun)
        {
            RunArguments = dotNetRun.RunArguments;
        }
    }

    public bool IsNew => _project is null;

    public string Title => IsNew ? "New project" : "Edit project";

    public string PrimaryButtonText => IsNew ? "Add" : "Save";

    public IReadOnlyList<ProjectKind> Kinds { get; } = ProjectKind.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPowerShell), nameof(IsDotNetRun))]
    public partial ProjectKind Kind { get; set; }

    // the kind is told by what it creates, as that of a project being edited creates the project itself.
    public bool IsPowerShell => Kind.Create(Guid.Empty, string.Empty, string.Empty) is PowerShellProject;

    public bool IsDotNetRun => Kind.Create(Guid.Empty, string.Empty, string.Empty) is DotNetRunProject;

    /// <summary>The arguments of <c>dotnet run</c> itself, which only projects run by it have.</summary>
    [ObservableProperty]
    public partial string RunArguments { get; set; } = string.Empty;

    /// <summary>The script run before each command, which only projects run by PowerShell have.</summary>
    [ObservableProperty]
    public partial string PrependScript { get; set; } = string.Empty;

    /// <summary>The script run after each command, which only projects run by PowerShell have.</summary>
    [ObservableProperty]
    public partial string AppendScript { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsWorkingFolderMissing))]
    public partial string WorkingFolder { get; set; } = string.Empty;

    public bool IsWorkingFolderMissing => !string.IsNullOrWhiteSpace(WorkingFolder) && !Directory.Exists(ExpandedWorkingFolder);

    // the environment variables in it are expanded when it is used, so it is checked the same way.
    private string ExpandedWorkingFolder => Environment.ExpandEnvironmentVariables(WorkingFolder.Trim());

    /// <summary>The environment variables commands are run with, in the order they were added.</summary>
    public ObservableCollection<EnvironmentVariableEditorViewModel> EnvironmentVariables { get; } = [];

    /// <summary>Whether commands are also run with the variables in the .env file in the working folder.</summary>
    [ObservableProperty]
    public partial bool LoadEnvFile { get; set; }

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

    public bool IsValid => !string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(WorkingFolder) && !IsWorkingFolderMissing && !HasEnvironmentVariablesError;

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
        var environmentVariables = FilledEnvironmentVariables.ToDictionary(static variable => variable.Name.Trim(), static variable => variable.Value, StringComparer.OrdinalIgnoreCase);
        var built = _project switch
        {
            null => Kind.Create(Guid.NewGuid(), DisplayName.Trim(), WorkingFolder.Trim()) with { EnvironmentVariables = environmentVariables, LoadEnvFile = LoadEnvFile },
            { } project => project with { DisplayName = DisplayName.Trim(), WorkingFolder = WorkingFolder.Trim(), EnvironmentVariables = environmentVariables, LoadEnvFile = LoadEnvFile },
        };

        // a script of only blank lines would still move what is typed down in the script that is run
        return built switch
        {
            PowerShellProject powerShell => powerShell with { PrependScript = PrependScript.TrimEnd(), AppendScript = AppendScript.TrimEnd() },
            DotNetRunProject dotNetRun => dotNetRun with { RunArguments = RunArguments.Trim() },
            _ => built,
        };
    }
}
