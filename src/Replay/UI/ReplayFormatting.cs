using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Il2CppRUMBLE.Managers;
using ReplayMod.Replay.Files;
using ReplayMod.Replay.Serialization;

namespace ReplayMod.Replay.UI;

public class ReplayFormatting
{
    public static string GetReplayName(ReplayInfo replayInfo, bool isClip = false)
    {
        string sceneName = Utilities.GetFriendlySceneName(replayInfo.Header.Scene);
        string customMapName = replayInfo.Header.CustomMap;
        
        var localPlayer = PlayerManager.instance.localPlayer;
        string localPlayerName = Utilities.CleanName(localPlayer.Data.GeneralData.PublicUsername);
        
        string opponent = replayInfo.Header.Players.FirstOrDefault(p => p.MasterId != localPlayer.Data.GeneralData.PlayFabMasterId)?.Name;
        string opponentName = !string.IsNullOrEmpty(opponent)
            ? Utilities.CleanName(opponent)
            : "Unknown";
        
        string clip = isClip ? "Clip_" : "" ;
        
        DateTime.TryParse(replayInfo.Header.Date, out var dateTime);
        string timestamp = dateTime.ToString("yyyy-MM-dd_hh-mm-ss");
        
        string matchFormat = $"Replay_{clip}{localPlayerName}-vs-{opponentName}_on_{sceneName}_{timestamp}.replay";
        
        if (!string.IsNullOrEmpty(customMapName))
            return $"Replay_{clip}{localPlayerName}-vs-{opponentName}_on_{customMapName}_{timestamp}.replay";

        return sceneName switch
        {
            "Ring" or "Pit" => matchFormat,
            "Park" => $"Replay_{clip}{sceneName}_{replayInfo.Header.Players.Length}P_{localPlayerName}_{timestamp}.replay",
            _ => $"Replay_{clip}{sceneName}_{localPlayerName}_{timestamp}.replay"
        };
    }
    
    public static string GetReplayDisplayName(string path, ReplaySerializer.ReplayHeader header, string alternativeName = null, bool showTitle = true)
    {
        var name = alternativeName ?? Path.GetFileNameWithoutExtension(path);

        if (header == null)
            return name;

        var pattern = name.StartsWith("Replay", StringComparison.OrdinalIgnoreCase) && showTitle
            ? header.Title
            : name;

        return FormatReplayString(pattern, header);
    }
    
    public static string FormatReplayString(string pattern, ReplaySerializer.ReplayHeader header)
    {
        var finalScene = GetMapName(header: header);
        
        var parsedDate = string.IsNullOrEmpty(header.Date)
            ? DateTime.MinValue
            : DateTime.Parse(header.Date, CultureInfo.InvariantCulture);
        var duration = TimeSpan.FromSeconds(header.Duration);
        
        string GetPlayer(int index) =>
            header?.Players != null &&
            index >= 0 &&
            index < header.Players.Length &&
            header.Players[index] != null
                ? $"<#FFF>{header.Players[index].Name}<#FFF>"
                : "";

        string durationStr = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = $"<#FFF>{header.Players?.FirstOrDefault(p => p is { WasHost: true })?.Name ?? "Unknown"}<#FFF>",
            ["Client"] = $"<#FFF>{header.Players?.FirstOrDefault(p => p is { WasHost: false })?.Name ?? "Unknown"}<#FFF>",
            ["LocalPlayer"] = $"<#FFF>{header.Players?[0]?.Name ?? "Unknown"}<#FFF>",
            ["Scene"] = finalScene,
            ["Map"] = finalScene,
            ["DateTime"] = parsedDate == DateTime.MinValue ? "Unknown Date" : parsedDate,
            ["PlayerCount"] = $"{header.Players?.Length ?? 0} Player{((header.Players?.Length ?? 0) == 1 ? "" : "s")}",
            ["Version"] = header.Version ?? "Unknown Version",
            ["StructureCount"] = (header.Structures?.Length.ToString() ?? "0") + " Structure" + ((header.Structures?.Length ?? 0) == 1 ? "" : "s"),
            ["MarkerCount"] = header.MarkerCount,
            ["AveragePing"] = header.AvgPing,
            ["MinimumPing"] = header.MinPing,
            ["MaximumPing"] = header.MaxPing,
            ["Title"] = !string.IsNullOrEmpty(header.Title) ? header.Title : "Unknown Title",
            ["Duration"] = header.Duration > 0 ? durationStr : "Unknown",
            ["FPS"] = header.TargetFPS
        };

        for (int i = 0; i < (header.Players?.Length ?? 0); i++)
            values[$"Player{i + 1}"] = GetPlayer(i);

        var regex = new Regex(@"\{(\w+)(?::([^}]+))?\}");

        return regex.Replace(pattern, match =>
        {
            var key = match.Groups[1].Value;
            var param = match.Groups[2].Success ? match.Groups[2].Value : null;

            if (key.Equals("PlayerList", StringComparison.OrdinalIgnoreCase))
            {
                int count = 3;

                if (!string.IsNullOrEmpty(param) && int.TryParse(param, out int parsed))
                    count = parsed;

                if (header.Players != null)
                    return ReplayFiles.BuildPlayerLine(header.Players, count);
            }

            if (values.TryGetValue(key, out var val))
            {
                if (val is DateTime dateTime && param != null)
                    return dateTime.ToString(param);
                return val.ToString();
            }

            return match.Value;
        });
    }    
    
    public static string GetMapName(string path = null, ReplaySerializer.ReplayHeader header = null)
    {
        if (string.IsNullOrEmpty(path) && header == null)
            return null;
        
        header ??= ReplayArchive.GetManifest(path);

        if (!string.IsNullOrEmpty(header.CustomMap))
        {
            if (header.CustomMap.StartsWith("1|"))
                return "Unknown Custom Map";

            if (header.Scene == "FlatLandSingle")
                return "FlatLand";
            
            return header.CustomMap;
        }

        return Utilities.GetFriendlySceneName(header.Scene);
    }
}