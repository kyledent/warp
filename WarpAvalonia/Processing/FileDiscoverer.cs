using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Warp;
using Warp.Tools;

namespace WarpAvalonia.Processing;

/// <summary>
/// Avalonia port of Warp.FileDiscoverer.
/// Watches a directory for new cryo-EM data files, incubates them until stable
/// (file size unchanged for 1s), then creates Movie/TiltSeries objects.
/// Replaces BackgroundWorker with Task.Run + CancellationToken.
/// FileSystemWatcher works cross-platform on .NET 8.
/// </summary>
public class FileDiscoverer
{
    private int ExceptionsLogged = 0;

    private List<(string Path, Stopwatch Timer, long Size)> Incubator = new();

    private string DataFolderPath = "";
    private string ProcessingFolderPath = "";
    private string ProcessingOrDataFolderPath => string.IsNullOrEmpty(ProcessingFolderPath) ? DataFolderPath : ProcessingFolderPath;
    private string FileExtension = "*.*";
    private bool DoRecursiveSearch = false;

    private CancellationTokenSource? DiscoveryCts;
    private Task? DiscoveryTask;

    private FileSystemWatcher? FileWatcher;
    private bool FileWatcherRaised = false;

    private int IncubationMilliseconds = 1000;
    private List<Movie> Ripe = new();

    public event Action? IncubationStarted;
    public event Action? IncubationEnded;
    public event Action? FilesChanged;

    private Dictionary<string, Task> CreationTasks = new();

    public FileDiscoverer()
    {
    }

    public void ChangePath(string newPath, string newProcessingPath, string newExtension, bool newRecursive)
    {
        IncubationEnded?.Invoke();

        // Cancel existing discovery
        Shutdown();

        DataFolderPath = newPath;
        ProcessingFolderPath = newProcessingPath;
        FileExtension = newExtension;
        DoRecursiveSearch = newRecursive;

        if (string.IsNullOrEmpty(DataFolderPath) || !Directory.Exists(DataFolderPath))
            return;

        lock (Incubator) Incubator.Clear();
        lock (Ripe) Ripe.Clear();

        FilesChanged?.Invoke();

        // Set up file watcher
        try
        {
            FileWatcher?.Dispose();
            FileWatcher = new FileSystemWatcher(DataFolderPath);
            FileWatcher.IncludeSubdirectories = newRecursive;
            FileWatcher.Filter = newExtension;
            FileWatcher.Changed += (_, _) => FileWatcherRaised = true;
            FileWatcher.Created += (_, _) => FileWatcherRaised = true;
            FileWatcher.Renamed += (_, _) => FileWatcherRaised = true;
            FileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FileWatcher setup failed: {ex.Message}");
        }

        // Start discovery loop
        DiscoveryCts = new CancellationTokenSource();
        var ct = DiscoveryCts.Token;
        DiscoveryTask = Task.Run(() => WorkLoop(ct), ct);
    }

    private void WorkLoop(CancellationToken ct)
    {
        string[]? particleFileNames = null;
        string matchingDir = Path.Combine(ProcessingOrDataFolderPath, "matching");
        if (Directory.Exists(matchingDir))
            particleFileNames = Directory.EnumerateFiles(matchingDir, "*.star", SearchOption.TopDirectoryOnly)
                .Select(p => Helper.PathToNameWithExtension(p)).ToArray();

        while (!ct.IsCancellationRequested)
        {
            Stopwatch watch = new();
            bool eventNeedsFiring = false;
            FileWatcherRaised = false;

            try
            {
                var searchOption = DoRecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (string fileName in Directory.EnumerateFiles(DataFolderPath, FileExtension, searchOption).ToArray())
                {
                    if (ct.IsCancellationRequested) return;

                    string name = Helper.PathToNameWithExtension(fileName);
                    if (name.Length > 0 && name[0] == '.') continue;

                    // Skip if already discovered
                    lock (Ripe)
                        if (Ripe.Exists(m => m.DataPath == fileName)) continue;

                    string processingFileName = Path.Combine(ProcessingOrDataFolderPath, name);
                    string nameXML = Path.ChangeExtension(processingFileName, ".xml");

                    if (!File.Exists(nameXML))
                    {
                        // File has no XML metadata — needs incubation
                        HandleIncubation(fileName, ct);
                    }
                    else
                    {
                        // File has XML — create Movie immediately
                        CreateMovieAsync(fileName, processingFileName, particleFileNames);
                    }

                    eventNeedsFiring = true;
                    if (!watch.IsRunning) watch.Start();

                    if (eventNeedsFiring && watch.ElapsedMilliseconds > 500)
                    {
                        watch.Reset();
                        eventNeedsFiring = false;
                        FilesChanged?.Invoke();
                    }
                }
            }
            catch (Exception exc)
            {
                Debug.WriteLine($"FileDiscoverer: {exc.Message}");
                if (ExceptionsLogged < 100)
                {
                    try { File.AppendAllText("filediscoverer.log", $"{DateTime.Now}: {exc}\n\n"); ExceptionsLogged++; }
                    catch { }
                }
            }

            // Wait for pending creation tasks
            Task[] pending;
            lock (CreationTasks) pending = CreationTasks.Values.ToArray();
            if (pending.Length > 0) Task.WaitAll(pending);

            if (eventNeedsFiring) FilesChanged?.Invoke();

            // Sleep until file watcher fires or incubation needs checking
            while (!IsIncubating() && !FileWatcherRaised && !ct.IsCancellationRequested)
                Thread.Sleep(50);
        }
    }

    private void HandleIncubation(string fileName, CancellationToken ct)
    {
        FileInfo info;
        try { info = new FileInfo(fileName); }
        catch { return; }

        (string Path, Stopwatch Timer, long Size)? current;
        lock (Incubator) current = Incubator.FirstOrDefault(t => t.Path == fileName);

        if (current == null || current.Value.Path == null)
        {
            // New file — start incubation
            var timer = Stopwatch.StartNew();
            lock (Incubator)
            {
                Incubator.Add((fileName, timer, info.Length));
                if (Incubator.Count == 1) IncubationStarted?.Invoke();
            }
        }
        else
        {
            bool canRead = false;
            try { File.OpenRead(fileName).Close(); canRead = true; } catch { }

            if (info.Length != current.Value.Size || !canRead)
            {
                // File changed — restart incubation
                lock (Incubator) Incubator.RemoveAll(t => t.Path == fileName);
                var timer = Stopwatch.StartNew();
                lock (Incubator) Incubator.Add((fileName, timer, info.Length));
            }
            else if (current.Value.Timer.ElapsedMilliseconds > IncubationMilliseconds)
            {
                // File stable — create Movie
                string processingFileName = Path.Combine(ProcessingOrDataFolderPath, Helper.PathToNameWithExtension(fileName));
                string[]? particleFileNames = null;
                string matchingDir = Path.Combine(ProcessingOrDataFolderPath, "matching");
                if (Directory.Exists(matchingDir))
                    particleFileNames = Directory.EnumerateFiles(matchingDir, "*.star", SearchOption.TopDirectoryOnly)
                        .Select(p => Helper.PathToNameWithExtension(p)).ToArray();

                CreateMovieAsync(fileName, processingFileName, particleFileNames);

                lock (Incubator)
                {
                    Incubator.RemoveAll(t => t.Path == fileName);
                    if (Incubator.Count == 0) IncubationEnded?.Invoke();
                }
            }
        }
    }

    private void CreateMovieAsync(string dataFileName, string processingFileName, string[]? particleFileNames)
    {
        lock (Ripe)
            if (Ripe.Exists(m => m.DataPath == dataFileName)) return;
        lock (CreationTasks)
            if (CreationTasks.ContainsKey(dataFileName)) return;

        // Limit concurrent creation tasks
        while (true)
        {
            lock (CreationTasks) { if (CreationTasks.Count <= 15) break; }
            Thread.Sleep(1);
        }

        Task creationTask = Task.Run(() =>
        {
            try
            {
                Movie created;
                bool isTomostar = dataFileName.Length >= 8 &&
                    dataFileName.Substring(dataFileName.Length - 8).ToLower().Contains("tomostar");

                string? dataDir = string.IsNullOrEmpty(ProcessingFolderPath) ? null : Helper.PathToFolder(dataFileName);

                if (isTomostar)
                    created = new TiltSeries(processingFileName, dataDir);
                else
                    created = new Movie(processingFileName, dataDir, particleFileNames);

                lock (Ripe)
                {
                    int insertAt = 0;
                    while (insertAt < Ripe.Count && Ripe[insertAt].DataPath.CompareTo(dataFileName) < 0)
                        insertAt++;
                    Ripe.Insert(insertAt, created);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create Movie for {dataFileName}: {ex.Message}");
            }

            lock (CreationTasks) CreationTasks.Remove(dataFileName);
        });

        lock (CreationTasks) CreationTasks[dataFileName] = creationTask;
    }

    public void Shutdown()
    {
        DiscoveryCts?.Cancel();
        FileWatcher?.Dispose();
        FileWatcher = null;

        try { DiscoveryTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        DiscoveryCts?.Dispose();
        DiscoveryCts = null;
    }

    public Movie[] GetImmutableFiles()
    {
        lock (Ripe) return Ripe.ToArray();
    }

    public bool IsIncubating()
    {
        lock (Incubator) return Incubator.Count > 0;
    }
}
