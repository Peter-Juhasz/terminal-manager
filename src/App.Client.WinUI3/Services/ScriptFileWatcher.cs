using App.Domain;
using App.Extensions.Node;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Reads the scripts of the well known files in a folder, and reads them again each time the files change, are created, deleted or renamed.
/// </summary>
/// <remarks>
/// Changes are read a moment after the last of them, as a file is often written in several steps, or saved by renaming another one over it.
/// A file still being written, and so locked, is read again a moment later.
/// </remarks>
public sealed partial class ScriptFileWatcher : IDisposable
{
    /// <summary>The kinds of files scripts are read from, in the order they are shown.</summary>
    public static IReadOnlyList<IScriptFileFormat> DefaultFormats { get; } = (IScriptFileFormat[])[new TerminalManagerScriptFile(), new PackageJsonScriptFile()];

    // how long after the last change the files are read, and how long after a locked one is read again
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    // how many times a locked file is read again before it is given up on, until it changes again
    private const int MaxAttempts = 5;

    private readonly string _folder;
    private readonly Project _project;
    private readonly IReadOnlyList<IScriptFileFormat> _formats;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _timer;
    private readonly Lock _lock = new();
    private readonly Lock _readLock = new();

    // the formats to be read again, with how many times each was tried; guarded by the lock
    private readonly Dictionary<IScriptFileFormat, int> _pending = [];

    // the scripts of each file read, by format; guarded by the lock
    private readonly Dictionary<IScriptFileFormat, ScriptFileSnapshot> _snapshots = [];

    private bool _isDisposed;

    /// <summary>Gets ready to watch the files in <paramref name="folder"/> for <paramref name="project"/>, which is started by <see cref="Start"/>.</summary>
    public ScriptFileWatcher(string folder, Project project, IReadOnlyList<IScriptFileFormat>? formats = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(project);

        _folder = folder;
        _project = project;
        _formats = (formats ?? DefaultFormats).Where(format => format.IsSupportedBy(project)).ToArray();
        _timer = new(OnTimer);

        if (_formats.Count == 0)
        {
            return;
        }

        try
        {
            _watcher = new(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            foreach (var name in _formats.SelectMany(static format => format.RelatedFileNames.Prepend(format.FileName)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _watcher.Filters.Add(name);
            }

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // a folder that doesn't exist, or can't be watched, such as on some network drives, is still read once
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>Reads the files in the background, and again each time they change; called once <see cref="Changed"/> is handled, so it is raised for the first read too.</summary>
    public void Start()
    {
        if (_formats.Count == 0)
        {
            return;
        }

        try
        {
            _watcher?.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // the files are still read once
        }

        ScheduleAll(TimeSpan.Zero);
    }

    /// <summary>Raised on a background thread when the scripts of any of the files changed, including when they are first read.</summary>
    public event EventHandler? Changed;

    /// <summary>The scripts of each file there is, in the order of the formats.</summary>
    public IReadOnlyList<ScriptFileSnapshot> Snapshots
    {
        get
        {
            lock (_lock)
            {
                return _formats.Where(_snapshots.ContainsKey).Select(format => _snapshots[format]).ToArray();
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) => Schedule(e.Name);

    // an editor may save by renaming the file it wrote over the one it saves, or the file may be renamed away
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Schedule(e.OldName);
        Schedule(e.Name);
    }

    // changes may have been missed, so all the files are read again
    private void OnWatcherError(object sender, ErrorEventArgs e) => ScheduleAll(Delay);

    private void Schedule(string? name)
    {
        if (name is null)
        {
            return;
        }

        var affected = _formats
            .Where(format => string.Equals(format.FileName, name, StringComparison.OrdinalIgnoreCase) || format.RelatedFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (affected.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var format in affected)
            {
                _pending[format] = 0;
            }

            Restart(Delay);
        }
    }

    private void ScheduleAll(TimeSpan delay)
    {
        lock (_lock)
        {
            foreach (var format in _formats)
            {
                _pending[format] = 0;
            }

            Restart(delay);
        }
    }

    // called with the lock taken
    private void Restart(TimeSpan delay)
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    // one read at a time, so one started later, for a later change, isn't shown before one started earlier
    private void OnTimer(object? state)
    {
        lock (_readLock)
        {
            ReadPending();
        }
    }

    private void ReadPending()
    {
        KeyValuePair<IScriptFileFormat, int>[] pending;
        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            pending = [.. _pending];
            _pending.Clear();
        }

        var changed = false;
        var retry = false;
        foreach (var (format, attempts) in pending)
        {
            ScriptFileSnapshot? snapshot;
            try
            {
                snapshot = Read(format);
            }
            catch (IOException) when (attempts + 1 < MaxAttempts)
            {
                // still being written; read again in a moment, unless it changes again first, which starts over
                lock (_lock)
                {
                    _pending.TryAdd(format, attempts + 1);
                }

                retry = true;
                continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                snapshot = new(format, Path.Combine(_folder, format.FileName), Scripts: [], Error: ex.Message);
            }

            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _snapshots.TryGetValue(format, out var previous);
                if (snapshot is null ? previous is null : snapshot.IsEquivalentTo(previous))
                {
                    continue;
                }

                if (snapshot is null)
                {
                    _snapshots.Remove(format);
                }
                else
                {
                    _snapshots[format] = snapshot;
                }

                changed = true;
            }
        }

        if (retry)
        {
            lock (_lock)
            {
                Restart(Delay);
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // null when there is no such file
    private ScriptFileSnapshot? Read(IScriptFileFormat format)
    {
        var path = Path.Combine(_folder, format.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path);
        var related = format.RelatedFileNames.Where(name => File.Exists(Path.Combine(_folder, name))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            return new(format, path, format.Parse(text, _project, related));
        }
        catch (Exception ex)
        {
            // the file may be written by anyone, so whatever it fails with is shown for it, rather than ending the app on this thread
            return new(format, path, Scripts: [], Error: ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _watcher?.Dispose();
        _timer.Dispose();
    }
}
