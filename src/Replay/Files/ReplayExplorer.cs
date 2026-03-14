using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using static UnityEngine.Mathf;

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
        NameAscending,
        NameDescending,
        
        DateNewestFirst,
        DateOldestFirst,
        
        DurationLongestFirst,
        DurationShortestFirst,
        
        MapAscending,
        PlayerCountDescending
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
        currentReplayEntries = GetEntries();
        
        currentIndex = Clamp(currentIndex, -1, currentReplayEntries.Count - 1);
    }

    public List<Entry> GetEntries(SortingType sorting = SortingType.DateNewestFirst)
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
            .Select(file => new Entry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FullPath = file,
                header = ReplayArchive.GetManifest(file),
                IsFolder = false,
            })
            .ToList();

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
        return sorting switch
        {
            SortingType.NameAscending => files.OrderBy(f => f.Name).ToList(),
            SortingType.NameDescending => files.OrderByDescending(f => f.Name).ToList(),

            SortingType.DateNewestFirst => files.OrderByDescending(f => File.GetLastWriteTimeUtc(f.FullPath)).ToList(),
            SortingType.DateOldestFirst => files.OrderBy(f => File.GetLastWriteTimeUtc(f.FullPath)).ToList(),

            SortingType.DurationLongestFirst => files.OrderByDescending(f => f.header.Duration).ToList(),
            SortingType.DurationShortestFirst => files.OrderBy(f => f.header.Duration).ToList(),

            SortingType.MapAscending => files.OrderBy(f => ReplayFormatting.GetMapName(header: f.header), StringComparer.OrdinalIgnoreCase).ToList(),
            SortingType.PlayerCountDescending => files.OrderByDescending(f => f.header.Players?.Length).ToList(),

            _ => files
        };
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