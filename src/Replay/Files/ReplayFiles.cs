using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Social.Phone;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using ReplayMod.Core;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using UnityEngine;
using UnityEngine.Events;
using static UnityEngine.Mathf;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.Files;

public static class ReplayFiles
{
    public static string replayFolder = $@"{MelonEnvironment.UserDataDirectory}\ReplayMod";
    
    public static ReplayTable table;
    public static bool metadataHidden = false;

    private static object metadataHeightRoutine;
    private static object metadataScaleRoutine;
    
    public static FileSystemWatcher replayWatcher;
    public static FileSystemWatcher metadataFormatWatcher;
    public static bool reloadQueued;
    public static bool suppressWatcher;

    public static ReplayExplorer explorer;

    public static Texture2D folderIcon;
    public static Texture2D replayIcon;

    public static ReplaySerializer.ReplayHeader currentHeader = null;

    
    // ----- Init -----
    
    public static void Init()
    {
        Directory.CreateDirectory(Path.Combine(replayFolder, "Replays"));
        
        EnsureDefaultFormats();
        StartWatchingReplays();

        explorer = new ReplayExplorer(Path.Combine(replayFolder, "Replays"));
    }

    public static void EnsureDefaultFormats()
    {
        void WriteIfNotExists(string filePath, string contents)
        {
            string path = Path.Combine(replayFolder, "Settings", filePath);
            if (!File.Exists(path))
                File.WriteAllText(path, contents);
        }

        string metadataFormatsFolder = Path.Combine(replayFolder, "Settings", "MetadataFormats");
        Directory.CreateDirectory(metadataFormatsFolder);
        
        string autoNameFormatsFolder = Path.Combine(replayFolder, "Settings", "AutoNameFormats");
        Directory.CreateDirectory(autoNameFormatsFolder);
        
        const string TagHelpText =
            "Available tags:\n" +
            "{Host}\n" +
            "{Client} - first non-host player\n" +
            "{LocalPlayer} - the person who recorded the replay\n" +
            "{Player#}\n" +
            "{Scene}\n" +
            "{Map} - same as {Scene}\n" +
            "{DateTime}\n" +
            "{PlayerCount} - e.g. '1 player', '3 players'\n" +
            "{PlayerList} - Can specify how many player names are shown\n" +
            "{AveragePing}\n" +
            "{MinimumPing} - The lowest ping in the recording\n" +
            "{MaximumPing} - The highest ping in the recording\n" +
            "{Version}\n" +
            "{StructureCount}\n" +
            "{MarkerCount}\n" +
            "{Duration}\n" +
            "{FPS} - Target FPS of the recording\n" +
            "\n" +
            "You can pass parameters to tags using ':'.\n" +
            "Example: {PlayerList:3}, {DateTime:yyyyMMdd}\n\n" +
            "###\n";
        
        WriteIfNotExists("MetadataFormats/metadata_gym.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\nDuration: {Duration}");
        WriteIfNotExists("MetadataFormats/metadata_park.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\n\n{Scene}\n-----------\nHost: {Host}\nPing: {AveragePing} ms ({MinimumPing}-{MaximumPing})\n\n{PlayerList:3}\nDuration: {Duration}");
        WriteIfNotExists("MetadataFormats/metadata_match.txt", TagHelpText + "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\n\n{Scene}\n-----------\nHost: {Host}\nPing: {AveragePing} ms ({MinimumPing}-{MaximumPing})\nDuration: {Duration}");
        
        WriteIfNotExists("AutoNameFormats/gym.txt", TagHelpText + "{LocalPlayer} - {Scene}");
        WriteIfNotExists("AutoNameFormats/park.txt", TagHelpText + "{Host} - {Scene}\n");
        WriteIfNotExists("AutoNameFormats/match.txt", TagHelpText + "{Host} vs {Client} - {Scene}");
    }
    
    
    // ----- Format Files -----

    public static string LoadFormatFile(string path)
    {
        string fullPath = Path.Combine(replayFolder, "Settings", path + ".txt");
        if (!File.Exists(fullPath)) return null;
        
        var lines = File.ReadAllLines(fullPath);
        int startIndex = Array.FindIndex(lines, line => line.Trim() == "###");
        if (startIndex == -1 || startIndex + 1 >= lines.Length) 
            return null;

        var formatLines = lines.Skip(startIndex + 1);
        return string.Join("\n", formatLines);
    }

    public static string GetMetadataFormat(string scene)
    {
        return scene switch
        {
            "Gym" => LoadFormatFile("MetadataFormats/metadata_gym"),

            "Park" => LoadFormatFile("MetadataFormats/metadata_park"),

            "Map0" or "Map1" => LoadFormatFile("MetadataFormats/metadata_match"),

            _ => "{Title}\n{DateTime:yyyy-MM-dd HH:mm:ss}\nVersion {Version}\nDuration: {Duration}\n\n{StructureCount}"
        };
    }
    
    // ----- Metadata -----
    
    static void StartWatchingReplays()
    {
        replayWatcher = new FileSystemWatcher(Path.Combine(replayFolder, "Replays"));

        replayWatcher.NotifyFilter =
            NotifyFilters.FileName |
            NotifyFilters.LastWrite |
            NotifyFilters.CreationTime |
            NotifyFilters.DirectoryName;

        replayWatcher.Created += OnReplayFolderChanged;
        replayWatcher.Deleted += OnReplayFolderChanged;
        replayWatcher.Renamed += OnReplayFolderChanged;
        
        replayWatcher.EnableRaisingEvents = true;
        replayWatcher.IncludeSubdirectories = true;

        metadataFormatWatcher = new FileSystemWatcher(Path.Combine(replayFolder, "Settings", "MetadataFormats"), "*.txt");

        metadataFormatWatcher.NotifyFilter = NotifyFilters.LastWrite;
        metadataFormatWatcher.Changed += OnFormatChanged;
        metadataFormatWatcher.EnableRaisingEvents = true;
    }

    static void OnReplayFolderChanged(object sender, FileSystemEventArgs e)
    {
        IEnumerator ReloadNextFrame()
        {
            yield return null;

            ReloadReplays();
            reloadQueued = false;
        }
        
        if (reloadQueued || suppressWatcher) return;
        
        reloadQueued = true;
        MelonCoroutines.Start(ReloadNextFrame());
    }

    static void OnFormatChanged(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(explorer.CurrentReplayPath) || currentHeader == null)
            return;

        var format = GetMetadataFormat(currentHeader.Scene);
        table.metadataText.text = ReplayFormatting.FormatReplayString(format, currentHeader);
        table.metadataText.ForceMeshUpdate();
    }
    
    public static void HideMetadata()
    {
        if (table.metadataText == null || metadataHidden) return;

        metadataHidden = true;
        
        if (metadataHeightRoutine != null)
            MelonCoroutines.Stop(metadataHeightRoutine);

        if (metadataScaleRoutine != null)
            MelonCoroutines.Stop(metadataScaleRoutine);
        
        metadataHeightRoutine = MelonCoroutines.Start(Utilities.LerpValue(
            () => table.desiredMetadataTextHeight,
            v => table.desiredMetadataTextHeight = v,
            Lerp,
            1.5229f,
            1f,
            Utilities.EaseInOut
        ));

        metadataScaleRoutine = MelonCoroutines.Start(Utilities.LerpValue(
            () => table.metadataText.transform.localScale,
            v => table.metadataText.transform.localScale = v,
            Vector3.Lerp,
            Vector3.zero,
            1.3f,
            Utilities.EaseInOut
        ));
    }

    public static void ShowMetadata()
    {
        if (table.metadataText == null || !metadataHidden) return;

        metadataHidden = false;
        
        if (metadataHeightRoutine != null)
            MelonCoroutines.Stop(metadataHeightRoutine);

        if (metadataScaleRoutine != null)
            MelonCoroutines.Stop(metadataScaleRoutine);
        
        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.desiredMetadataTextHeight,
            v => table.desiredMetadataTextHeight = v,
            Lerp,
            1.9514f,
            1.3f,
            Utilities.EaseInOut
        ));

        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.metadataText.transform.localScale,
            v => table.metadataText.transform.localScale = v,
            Vector3.Lerp,
            Vector3.one * 0.25f,
            1.3f,
            Utilities.EaseInOut
        ));
    }

    public static string BuildPlayerLine(PlayerInfo[] players, int maxNames)
    {
        if (players == null || players.Length == 0)
            return string.Empty;

        int count = players.Length;
        int shown = Math.Min(count, maxNames);

        var names = new List<string>(shown);
        for (int i = 0; i < shown; i++)
        {
            if (players[i] == null) continue;
            names.Add($"{players[i].Name}<#FFF>");
        }

        string line = string.Join(", ", names);

        if (count > maxNames)
            line += $" +{count - maxNames} others";

        return
            $"{count} player{(count == 1 ? "" : "s")}\n" +
            $"{line}\n";
    }
    
    
    // ----- Replay Selection -----
    
    private static void SelectReplayFromExplorer()
    {
        if (table.replayNameText == null)
            return;

        var path = explorer.CurrentReplayPath;
        var count = explorer.currentReplayEntries.Count(e => !e.IsFolder);
        var index = explorer.currentIndex;
        var shownIndex = index < 0 
            ? 0 
            : explorer.currentReplayEntries.Take(index + 1).Count(e => !e.IsFolder);

        ReplaySerializer.ReplayHeader header = null;

        if (string.IsNullOrEmpty(path))
        {
            currentHeader = null;

            table.replayNameText.text = "No Replay Selected";
            HideMetadata();
            ReplaySettings.replayExplorerGO.SetActive(true);
            ReplaySettings.replaySettingsGO.SetActive(false);

            explorer.Refresh();
            RefreshUI();
        }
        else
        {
            try
            {
                header = explorer.currentlySelectedEntry.header;
                currentHeader = header;

                table.replayNameText.text = ReplayFormatting.GetReplayDisplayName(path, header);

                Main.instance.replaySettings.Show(path);

                var format = GetMetadataFormat(header.Scene);
                table.metadataText.text = ReplayFormatting.FormatReplayString(format, header);

                ShowMetadata();
            }
            catch (Exception e)
            {
                Main.ReplayError($"Failed to load replay `{path}`: {e}");

                explorer.Select(-1);
                currentHeader = null;

                table.replayNameText.text = "Invalid Replay";
                HideMetadata();
                ReplaySettings.replayExplorerGO.SetActive(true);
                ReplaySettings.replaySettingsGO.SetActive(false);

                explorer.Refresh();
                RefreshUI();
            }
        }

        table.indexText.text = $"{shownIndex} / {count}";
        
        ReplayAPI.ReplaySelectedInternal(header, path);
        
        table.replayNameText.ForceMeshUpdate();
        table.indexText.ForceMeshUpdate();
        table.metadataText.ForceMeshUpdate();
        ApplyTMPSettings(table.indexText, 5f, 0.51f, false);
        ApplyTMPSettings(table.metadataText, 15f, 2f, true);
        table.replayNameText.GetComponent<RectTransform>().sizeDelta = new Vector2(3, 0.7f);
        ReplaySettings.replayExplorerGO.transform.GetChild(6).GetChild(1)
            .GetComponent<RectTransform>().sizeDelta = new Vector2(0.51f, 0.11f);
        table.metadataText.enableAutoSizing = true;
    }

    public static void ReloadReplays()
    {
        if (ReplaySettings.replayExplorerGO == null)
            return;
        
        var previousGuid = currentHeader?.Guid;

        explorer.currentPage = 0;
        explorer.Refresh();

        if (explorer.pageCount == 0)
        {
            explorer.Select(-1);
            ReplaySettings.replayExplorerGO.SetActive(true);
            ReplaySettings.replaySettingsGO.SetActive(false);
            ReplaySettings.replayExplorerGO.transform.parent.parent.gameObject.SetActive(false);
            return;
        }
            
        ReplaySettings.replayExplorerGO.transform.parent.parent.gameObject.SetActive(true);

        if (!string.IsNullOrEmpty(previousGuid))
        {
            int newIndex = explorer.currentReplayEntries
                .Select((entry, i) => new { entry, i })
                .FirstOrDefault(x => x.entry.header?.Guid == previousGuid)?.i ?? -1;

            explorer.Select(newIndex);
        }
        else
        {
            explorer.Select(-1);
            ReplaySettings.replayExplorerGO.SetActive(true);
            ReplaySettings.replaySettingsGO.SetActive(false);
        }
        
        RefreshUI();
        SelectReplayFromExplorer();
    }
    
    public static void RefreshUI()
    {
        if (ReplaySettings.replayExplorerGO == null)
            return;
        
        var page = explorer.GetPage();
        var tags = ReplaySettings.replayExplorerGO.GetComponentsInChildren<PlayerTag>(true);

        ReplaySettings.replayExplorerGO.transform.GetChild(7).GetChild(0)
            .GetComponent<TextMeshPro>().text = Utilities.FormatPage(explorer.currentPage, explorer.pageCount);

        ReplaySettings.replayExplorerGO.transform.GetChild(6).GetChild(1)
            .GetComponent<TextMeshPro>().text = Path.GetRelativePath(replayFolder, explorer.CurrentFolderPath);

        // Stupid size delta gets reset after calling this so I have to
        // make a *whole* coroutine for it...
        MelonCoroutines.Start(ApplySizeDelta());

        IEnumerator ApplySizeDelta() {
            yield return null;
            
            ReplaySettings.replayExplorerGO.transform.GetChild(6).GetChild(1)
                .GetComponent<RectTransform>().sizeDelta = new Vector2(0.51f, 0.11f);
        }
        
        table.indexText.text = $"0 / {explorer.currentReplayEntries.Count(e => !e.IsFolder)}";
        table.indexText.ForceMeshUpdate();
        
        for (int i = 0; i < tags.Length; i++)
        {
            var index = i;
            var tag = tags[index];
            var button = tag.transform.GetChild(0).GetComponent<InteractionButton>();

            if (index >= page.Count)
            {
                tag.gameObject.SetActive(false);
                continue;
            }

            tag.gameObject.SetActive(true);

            var entry = page[index];
            var globalIndex = explorer.currentReplayEntries.IndexOf(entry);

            tag.username.text = ReplayFormatting.GetReplayDisplayName(entry.FullPath, entry.header, entry.Name);
            tag.username.ForceMeshUpdate();
            
            button.OnPressed.RemoveAllListeners();
            button.OnPressed.AddListener((UnityAction)(() => { SelectReplay(globalIndex); }));

            var icon = button.transform.GetChild(1).GetChild(3).GetComponent<MeshRenderer>();
            icon.material.SetTexture("_Texture", entry.IsFolder ? folderIcon : replayIcon);
            icon.transform.localScale = (entry.IsFolder ? Vector3.one * 0.0522f : new Vector3(0.0422f, 0.0402f, 0.0422f));
            
            if (!entry.IsFolder)
                button.transform.GetChild(1).GetChild(7).gameObject.SetActive(entry.header.isFavorited);
        }
    }
    
    public static void SelectReplay(int index)
    {
        if (explorer.Select(index))
            SelectReplayFromExplorer();
        else
            RefreshUI();
    }

    public static void NextReplay()
    {
        explorer.Next();
        SelectReplayFromExplorer();
    }

    public static void PreviousReplay()
    {
        explorer.Previous();
        SelectReplayFromExplorer();
    }
    
    public static void LoadReplays()
    {
        explorer.Refresh();
        SelectReplayFromExplorer();
    }
    
    static void ApplyTMPSettings(TextMeshPro text, float horizontal, float vertical, bool apply)
    {
        if (text == null) return;
        
        text.horizontalAlignment = HorizontalAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, horizontal);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, vertical);

        if (apply)
        {
            text.fontSizeMin = 0.1f;
            text.fontSizeMax = 7f;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = true;
            text.enableWordWrapping = false;
        }
    }
}