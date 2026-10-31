using App.Client.WinUI3.Models;
using CommunityToolkit.Mvvm.ComponentModel;

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
    }

    /// <summary>Starts editing <paramref name="project"/>, whose kind can't be changed.</summary>
    public ProjectEditorViewModel(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _project = project;
        Kind = new ProjectKind(project.TerminalDisplayName, (_, _, _) => project);
        Kinds = (ProjectKind[])[Kind];
        DisplayName = project.DisplayName;
        WorkingFolder = project.WorkingFolder;
    }

    public bool IsNew => _project is null;

    public string Title => IsNew ? "New project" : "Edit project";

    public string PrimaryButtonText => IsNew ? "Add" : "Save";

    public IReadOnlyList<ProjectKind> Kinds { get; } = ProjectKind.All;

    [ObservableProperty]
    public partial ProjectKind Kind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsWorkingFolderMissing))]
    public partial string WorkingFolder { get; set; } = string.Empty;

    public bool IsWorkingFolderMissing => !string.IsNullOrWhiteSpace(WorkingFolder) && !Directory.Exists(ExpandedWorkingFolder);

    // the environment variables in it are expanded when it is used, so it is checked the same way.
    private string ExpandedWorkingFolder => Environment.ExpandEnvironmentVariables(WorkingFolder.Trim());

    public bool IsValid => !string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(WorkingFolder) && !IsWorkingFolderMissing;

    public Project Build() => _project switch
    {
        null => Kind.Create(Guid.NewGuid(), DisplayName.Trim(), WorkingFolder.Trim()),
        { } project => project with { DisplayName = DisplayName.Trim(), WorkingFolder = WorkingFolder.Trim() },
    };
}
