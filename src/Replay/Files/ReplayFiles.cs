using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using ReplayMod.Core;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using UnityEngine;
using static UnityEngine.Mathf;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.Files;

public static class ReplayFiles
{
    public static string replayFolder = $"{MelonEnvironment.UserDataDirectory}/ReplayMod";
    
    public static ReplayTable table;
    public static bool metadataLerping = false;
    public static bool metadataHidden = false;
    
    public static FileSystemWatcher replayWatcher;
    public static FileSystemWatcher metadataFormatWatcher;
    public static bool reloadQueued;
    public static bool suppressWatcher;

    public static ReplayExplorer explorer;

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
        replayWatcher = new FileSystemWatcher(Path.Combine(replayFolder, "Replays"), "*.replay");

        replayWatcher.NotifyFilter =
            NotifyFilters.FileName |
            NotifyFilters.LastWrite |
            NotifyFilters.CreationTime;

        replayWatcher.Created += OnReplayFolderChanged;
        replayWatcher.Deleted += OnReplayFolderChanged;
        replayWatcher.Renamed += OnReplayFolderChanged;
        
        replayWatcher.EnableRaisingEvents = true;

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
        if (table.metadataText == null || metadataLerping) return;

        metadataLerping = true;
        metadataHidden = true;
        
        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.desiredMetadataTextHeight,
            v => table.desiredMetadataTextHeight = v,
            Lerp,
            1.5229f,
            1f,
            Utilities.EaseInOut
        ));

        MelonCoroutines.Start(Utilities.LerpValue(
            () => table.metadataText.transform.localScale,
            v => table.metadataText.transform.localScale = v,
            Vector3.Lerp,
            Vector3.zero,
            1.3f,
            Utilities.EaseInOut,
            () => metadataLerping = false
        ));
    }

    public static void ShowMetadata()
    {
        if (table.metadataText == null || metadataLerping) return;

        metadataLerping = true;
        
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
            Utilities.EaseInOut,
            () => metadataLerping = false
        ));
        
        metadataHidden = false;
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
        var count = explorer.currentReplayPaths.Count;
        var index = explorer.currentIndex;

        ReplaySerializer.ReplayHeader header = null;

        if (string.IsNullOrEmpty(path))
        {
            currentHeader = null;

            table.replayNameText.text = "No Replay Selected";
            table.indexText.text = $"(0 / {count})";
            HideMetadata();
            Main.instance.replaySettings.gameObject.SetActive(false);
        }
        else
        {
            int shownIndex = index < 0 ? 0 : index + 1;
            table.indexText.text = $"({shownIndex} / {count})";

            try
            {
                header = explorer.currentlySelectedEntry.header;
                currentHeader = header;

                table.replayNameText.text = ReplayFormatting.GetReplayDisplayName(path, header);

                Main.instance.replaySettings.Show(path);

                var format = GetMetadataFormat(header.Scene);
                table.metadataText.text = ReplayFormatting.FormatReplayString(format, header);

                ShowMetadata();
                Main.instance.replaySettings.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                Main.ReplayError($"Failed to load replay `{path}`: {e}");

                explorer.Select(-1);
                currentHeader = null;

                table.replayNameText.text = "Invalid Replay";
                table.indexText.text = $"(0 / {count})";
                HideMetadata();
                Main.instance.replaySettings.gameObject.SetActive(false);
            }
        }

        ReplayAPI.ReplaySelectedInternal(header, path);
        
        table.replayNameText.ForceMeshUpdate();
        table.indexText.ForceMeshUpdate();
        table.metadataText.ForceMeshUpdate();
        ApplyTMPSettings(table.replayNameText, 5f, 0.51f, true);
        ApplyTMPSettings(table.indexText, 5f, 0.51f, false);
        ApplyTMPSettings(table.metadataText, 15f, 2f, true);
        table.metadataText.enableAutoSizing = true;
    }

    public static void ReloadReplays()
    {
        var previousGuid = currentHeader?.Guid;

        explorer.Refresh();

        if (!string.IsNullOrEmpty(previousGuid))
        {
            int newIndex = explorer.currentReplayPaths
                .Select((path, i) => new { path, i })
                .FirstOrDefault(x =>
                {
                    var header = ReplayArchive.GetManifest(x.path);
                    return header.Guid == previousGuid;
                })?.i ?? -1;

            explorer.Select(newIndex);
        }
        else
        {
            explorer.Select(-1);
        }
        
        SelectReplayFromExplorer();
    }
    
    public static void SelectReplay(int index)
    {
        explorer.Select(index);
        SelectReplayFromExplorer();
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
            text.autoSizeTextContainer = true;
        }
    }
}