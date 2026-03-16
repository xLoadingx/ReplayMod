using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using static UnityEngine.Mathf;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.Files;

public class ReplayExplorer
{
    public string RootPath { get; }
    public string CurrentFolderPath { get; private set; }

    public List<Entry> currentReplayEntries = new();
    public int currentIndex = -1;

    public const int pageSize = 6;
    public int currentPage = 0;
    public int pageCount => Utilities.GetPageCount(currentReplayEntries.Count, pageSize);

    public enum SortingType
    {
        Name,
        Date,
        Duration,
        Map,
        PlayerCount,
        OpponentBP,
        MarkerDensity
    }

    public string CurrentReplayPath =>
        currentIndex >= 0 && currentIndex < currentReplayEntries.Count
            ? currentReplayEntries[currentIndex].FullPath
            : null;

    public Entry currentlySelectedEntry =>
        currentIndex >= 0 && currentIndex < currentReplayEntries.Count
            ? currentReplayEntries[currentIndex]
            : null;

    public ReplayExplorer(string root)
    {
        RootPath = root;
        CurrentFolderPath = root;
        Refresh();
    }

    public void Refresh()
    {
        Enum.TryParse((string)Main.instance.ExplorerSorting.Value, true, out SortingType sortingType);
        currentReplayEntries = GetEntries(sortingType);
        
        currentIndex = Clamp(currentIndex, -1, currentReplayEntries.Count - 1);
        
        ReplayAPI.ExplorerRefreshedInternal();
    }

    public List<Entry> GetEntries(SortingType sorting = SortingType.Date)
    {
        var folders = Directory
            .GetDirectories(CurrentFolderPath)
            .Select(dir => new Entry
            {
                Name = Path.GetFileName(dir), 
                FullPath = dir, 
                IsFolder = true
            })
            .OrderBy(e => e.Name)
            .ToList();
        
        var files = Directory
            .GetFiles(CurrentFolderPath, "*.replay")
            .Select(file =>
            {
                ReplaySerializer.ReplayHeader header;

                try
                {
                    header = ReplayArchive.GetManifest(file);
                }
                catch
                {
                    return null;
                }
                
                return new Entry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FullPath = file,
                    header = header,
                    IsFolder = false
                };
            })
            .ToList();

        files = SortFiles(files, sorting);

        var entries = new List<Entry>();

        if (CurrentFolderPath != RootPath)
        {
            entries.Add(new Entry
            {
                Name = "..",
                FullPath = Directory.GetParent(CurrentFolderPath)?.FullName,
                IsFolder = true
            });
        }

        entries.AddRange(folders);
        entries.AddRange(files);

        return entries;
    }

    private List<Entry> SortFiles(List<Entry> files, SortingType sorting)
    {
        IOrderedEnumerable<Entry> query =
            (bool)Main.instance.FavoritesFirst.SavedValue
                ? files.OrderByDescending(f => f.header?.isFavorited ?? false)
                : files.OrderBy(f => 0);
        
        query = sorting switch
        {
            SortingType.Name => query.ThenBy(f => f.Name),
            SortingType.Date => query.ThenByDescending(f => File.GetLastWriteTimeUtc(f.FullPath)),
            SortingType.Duration => query.ThenByDescending(f => f.header?.Duration ?? 0),
            SortingType.Map => query.ThenBy(f => ReplayFormatting.GetMapName(header: f.header), StringComparer.OrdinalIgnoreCase),
            SortingType.PlayerCount => query.ThenByDescending(f => f.header?.Players?.Length ?? 0),
            SortingType.OpponentBP => query.ThenByDescending(f => f.header?.Players
                .Where(p => !p.IsLocal)
                .Select(p => p.BattlePoints)
                .DefaultIfEmpty(0)
                .Max()),
            SortingType.MarkerDensity => query.OrderByDescending(f =>
            {
                int markerCount = f.header?.Markers?.Length ?? 0;
                float duration = f.header?.Duration ?? 0;

                return markerCount / duration * Log(markerCount + 1);
            }),
            _ => query
        };

        var newFiles = query.ToList();
        
        if ((bool)Main.instance.SortingDirection.Value)
            newFiles.Reverse();
        
        return newFiles;
    }

    public List<Entry> GetPage()
    {
        return currentReplayEntries
            .Skip(currentPage * pageSize)
            .Take(pageSize)
            .ToList();
    }

    public void Enter(string path)
    {
        if (!Directory.Exists(path)) 
            return;

        if (!path.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase)) 
            return;
        
        CurrentFolderPath = path;
        currentIndex = -1;
        Refresh();

        ReplayAPI.ExplorerFolderChangedInternal(CurrentFolderPath);
        ReplayAPI.ReplaySelectedInternal(null, CurrentFolderPath);
    }

    public void GoUp()
    {
        if (CurrentFolderPath == RootPath)
            return;
        
        var parent = Directory.GetParent(CurrentFolderPath);
        if (parent == null)
            return;
        
        CurrentFolderPath = parent.FullName;
        currentIndex = -1;
        Refresh();

        ReplayAPI.ExplorerFolderChangedInternal(CurrentFolderPath);
        ReplayAPI.ReplaySelectedInternal(null, CurrentFolderPath);
    }

    public void Next()
    {
        if (currentReplayEntries.Count == 0) return;

        if (GetPage().All(e => e.IsFolder)) return;

        do
        {
            currentIndex++;

            if (currentIndex > currentReplayEntries.Count - 1)
                currentIndex = -1;
        } while (currentIndex != -1 && currentReplayEntries[currentIndex].IsFolder);
    }

    public void Previous()
    {
        if (currentReplayEntries.Count == 0) return;

        if (GetPage().All(e => e.IsFolder)) return;
        
        do
        {
            currentIndex--;

            if (currentIndex < -1)
                currentIndex = currentReplayEntries.Count - 1;
        } while (currentIndex != -1 && currentReplayEntries[currentIndex].IsFolder);
    }

    // Returns true if selection is a replay
    public bool Select(int index)
    {
        if (index < 0 || index >= currentReplayEntries.Count)
            return false;

        var entry = currentReplayEntries[index];

        if (entry.IsFolder)
        {
            Enter(entry.FullPath);
            return false;
        }

        currentIndex = index;
        return true;
    }

    public class Entry
    {
        public string Name;
        public string FullPath;
        public ReplaySerializer.ReplayHeader header;
        public bool IsFolder;
    }
}