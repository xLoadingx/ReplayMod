using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Social;
using Il2CppRUMBLE.Social.Phone;
using Il2CppTMPro;
using MelonLoader;
using ReplayMod.Core;
using ReplayMod.Replay.Serialization;
using UnityEngine;
using static UnityEngine.Mathf;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.UI;

[RegisterTypeInIl2Cpp]
public class ReplaySettings : MonoBehaviour
{
    private string currentPath;
    private ReplaySerializer.ReplayHeader currentHeader;
    
    public static TextMeshPro replayName;
    public static TextMeshPro dateText;
    public static TextMeshPro renameInstructions;
    public static TextMeshPro durationComp;
    
    public static InteractionButton renameButton;
    public static InteractionButton deleteButton;
    public static InteractionButton copyPathButton;
    
    public static GameObject povButton;
    public static GameObject hideLocalPlayerToggle;
    public static GameObject openControlsButton;
    
    public static bool hideLocalPlayer = true;
    public static TextMeshPro pageNumberText;
    
    public static bool selectionInProgress;
    public static Player selectedPlayer;
    public static Dictionary<int, List<(UserData, Player)>> playerList = new();
    public static List<PlayerTag> playerTags = new();
    public static int currentPlayerPage = 0;

    public static GameObject slideOutPanel;

    public static GameObject timeline;

    private bool isRenaming = false;
    private StringBuilder renameBuffer = new();
    private string rawReplayName;

    public void Show(string path)
    {
        povButton.SetActive(Main.Playback.isPlaying);
        hideLocalPlayerToggle.SetActive(Main.Playback.isPlaying);
        openControlsButton.SetActive(Main.Playback.isPlaying);
        
        currentPath = path;
        currentHeader = ReplayArchive.GetManifest(path);

        rawReplayName = Path.GetFileNameWithoutExtension(path);
        
        replayName.text = ReplayFormatting.GetReplayDisplayName(path, currentHeader);
        replayName.ForceMeshUpdate();
        
        renameBuffer.Clear();
        renameBuffer.Append(rawReplayName);

        dateText.text = ReplayFormatting.FormatReplayString("{DateTime:yyyy/MM/dd hh:mm tt}", currentHeader);
        dateText.ForceMeshUpdate();
        
        slideOutPanel.SetActive(false);
        slideOutPanel.transform.localPosition = new Vector3(0.1709f, 0.5273f, 0.16f);

        timeline.transform.GetChild(0).GetComponent<TimelineScrubber>().header = currentHeader;
        timeline.GetComponent<MeshRenderer>().material.SetFloat("_BP_Target", currentHeader.Duration * 1000f);
        Utilities.AddMarkers(currentHeader, timeline.GetComponent<MeshRenderer>(), false);
        
        renameButton.SetButtonToggleStatus(false, withEvents: true);
        renameInstructions.gameObject.SetActive(false);
        isRenaming = false;

        TimeSpan t = TimeSpan.FromSeconds(currentHeader.Duration);
        
        durationComp.text = t.TotalHours >= 1 ? 
            $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : 
            $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        
        durationComp.ForceMeshUpdate();
        
        isRenaming = false;
    }

    private void Update()
    {
        if (!isRenaming)
            return;
        
        if (Input.GetKeyDown(KeyCode.Return))
        {
            TryRename(renameBuffer.ToString());
            isRenaming = false;
            renameInstructions.gameObject.SetActive(false);
            renameButton.SetButtonToggleStatus(false, true);
            return;
        } 
        
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            replayName.text = ReplayFormatting.GetReplayDisplayName(currentPath, currentHeader);
            replayName.ForceMeshUpdate();
            renameButton.SetButtonToggleStatus(false, true);
            renameInstructions.gameObject.SetActive(false);
            isRenaming = false;
            return;
        }

        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (renameBuffer.Length > 0)
                {
                    renameBuffer.Remove(renameBuffer.Length - 1, 1);
                    AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], transform.position);
                }
            } else if (IsAllowedChar(c))
            {
                renameBuffer.Append(c);
                AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardUnlocked"], transform.position);
            }
            else
            {
                Main.ReplayError();
            }
        }

        replayName.text = ReplayFormatting.GetReplayDisplayName(currentPath, currentHeader, renameBuffer.ToString(), false);
        replayName.ForceMeshUpdate();
    }

    public void OnRenamePressed(bool toggleState)
    {
        if (toggleState)
        {
            replayName.text = ReplayFormatting.GetReplayDisplayName(currentPath, currentHeader);
            replayName.ForceMeshUpdate();
            isRenaming = false;
            renameInstructions.gameObject.SetActive(false);
        }
        else
        {
            isRenaming = true;
            renameBuffer.Clear();
            renameBuffer.Append(rawReplayName);
            
            renameInstructions.gameObject.SetActive(true);
        }
    }

    private bool IsAllowedChar(char c)
    {
        char[] blocked = Path.GetInvalidFileNameChars();
        return !blocked.Contains(c);
    }

    private void TryRename(string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            Main.ReplayError($"Invalid replay name ({newName})", transform.position);
            return;
        }
        
        string dir = Path.GetDirectoryName(currentPath);
        string newPath = Path.Combine(dir, newName + ".replay");

        if (File.Exists(newPath))
        {
            Main.ReplayError($"Name already exists ({newPath})", transform.position);
            return;
        }

        File.Move(currentPath, newPath);
        currentPath = newPath;
        Show(currentPath);

        ReplayAPI.ReplayRenamedInternal(currentHeader, newPath);

        AudioManager.instance.Play(ReplayCache.SFX["Call_PoseGhost_MovePerformed"], transform.position);
    }
    
        public static Dictionary<int, List<(UserData, Player)>> PaginateReplay(
        ReplaySerializer.ReplayHeader header, 
        ReplayPlayback.Clone[] PlaybackPlayers, 
        bool includeLocalPlayer = true
    )
    {
        var allEntries = new List<(UserData, Player)>();

        if (includeLocalPlayer)
        {
            var local = Main.LocalPlayer;
            allEntries.Add((
                new UserData(
                    PlatformManager.CurrentPlatform,
                    local.Data.GeneralData.PlayFabMasterId,
                    Guid.NewGuid().ToString(),
                    $"You ({local.Data.GeneralData.PublicUsername})",
                    local.Data.GeneralData.BattlePoints
                ),
                local
            ));
        }

        var players = header.Players;

        for (int i = 0; i < players.Length; i++)
        {
            if (header.Players[i] == null) continue;
            
            allEntries.Add((
                new UserData(
                    PlatformManager.Platform.Unknown,
                     $"{players[i].MasterId}_{Guid.NewGuid().ToString()}",
                    Guid.NewGuid().ToString(),
                    players[i].Name,
                    players[i].BattlePoints
                ),
                PlaybackPlayers[i].Controller.assignedPlayer
            ));
        }

        var result = new Dictionary<int, List<(UserData, Player)>>();
        int pageCount = CeilToInt(allEntries.Count / 4f);

        for (int i = 0; i < pageCount; i++)
        {
            var page = new List<(UserData, Player)>();

            for (int j = 0; j < 4; j++)
            {
                int index = i * 4 + j;
                if (index >= allEntries.Count)
                    break;

                page.Add(allEntries[index]);
            }

            result[i] = page;
        }

        return result;
    }

    public static IEnumerator SelectPlayer(Action<Player> callback, float afterSelectionDelay)
    {
        if (selectionInProgress)
            yield break;
        
        selectionInProgress = true;
        selectedPlayer = null;

        TogglePlayerSelection(true);

        while (selectedPlayer == null && selectionInProgress)
            yield return null;

        yield return new WaitForSeconds(afterSelectionDelay);
        
        TogglePlayerSelection(false);
        
        callback?.Invoke(selectedPlayer);
        selectionInProgress = false;
    }

    public static void TogglePlayerSelection(bool active)
    {
        slideOutPanel.SetActive(true);

        if (active)
            SelectPlayerPage(0);

        if (!active)
            selectionInProgress = false;
        
        AudioManager.instance.Play(ReplayCache.SFX[active ? "Call_Phone_ScreenUp" : "Call_Phone_ScreenDown"], slideOutPanel.transform.localPosition);

        Vector3 position = active ? new Vector3(-1.1906f, 0.5273f, 0.16f) : new Vector3(-0.1288f, 0.5273f, 0.16f);
        MelonCoroutines.Start(Utilities.LerpValue(
            () => slideOutPanel.transform.localPosition,
            v => slideOutPanel.transform.localPosition = v,
            Vector3.Lerp,
            position,
            0.8f,
            Utilities.EaseIn,
            () => { if (!active) slideOutPanel.SetActive(false); }
        ));
    }

    public static (UserData data, Player player) PlayerAtIndex(int index) 
        => playerList.TryGetValue(currentPlayerPage, out var list) ? (index >= 0 && index < list.Count ? list[index] : (null, null)) : (null, null);

    public static void SelectPlayerPage(int page)
    {
        int maxPage = Max(0, playerList.Count - 1);
        currentPlayerPage = Clamp(page, 0, maxPage);

        pageNumberText.text = $"{currentPlayerPage + (playerList.Count == 0 ? 0 : 1)} / {playerList.Count}";
        pageNumberText.ForceMeshUpdate();

        var usersOnPage = playerList.TryGetValue(currentPlayerPage, out var value) ? value : new List<(UserData, Player)>();

        for (int i = 0; i < playerTags.Count; i++)
        {
            if (i < usersOnPage.Count)
            {
                playerTags[i].gameObject.SetActive(true);
                playerTags[i].Initialize(usersOnPage[i].Item1);
            }
            else
            {
                playerTags[i].gameObject.SetActive(false);
            }
        }
    }
    
    [RegisterTypeInIl2Cpp]
    public class TimelineScrubber : MonoBehaviour
    {
        public ReplaySerializer.ReplayHeader header;
    
        private void OnTriggerEnter(Collider other)
        {
            if (!IsFinger(other.gameObject, out var isLeft))
                return;

            AudioManager.instance.Play(ReplayCache.SFX["Call_GearMarket_GenericButton_Press"], transform.position);
        
            if ((bool)Main.instance.EnableHaptics.SavedValue)
                Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(0.6f, isLeft ? 0.15f : 0f, 0.6f, !isLeft ? 0.15f : 0f);
        }

        private void OnTriggerStay(Collider other)
        {
            if (!IsFinger(other.gameObject, out _))
                return;
        
            Vector3 point = other.ClosestPointOnBounds(transform.position);
            float u = Utilities.GetProgressFromMeshPosition(point, GetComponentInParent<MeshRenderer>());

            float time = u * header.Duration;
        
            GetComponentInParent<MeshRenderer>().material?.SetFloat("_BP_Current", time * 1000f);
        
            if (Main.Playback.isPlaying)
                Main.Playback.SetPlaybackTime(time);
        }
    
        private void OnTriggerExit(Collider other)
        {
            if (!IsFinger(other.gameObject, out var isLeft))
                return;

            AudioManager.instance.Play(ReplayCache.SFX["Call_Interactionbase_ButtonRelease"], transform.position);
        
            if ((bool)Main.instance.EnableHaptics.SavedValue)
                Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(0.3f, isLeft ? 0.15f : 0f, 0.3f, !isLeft ? 0.15f : 0f);
        }

        private bool IsFinger(GameObject obj, out bool isLeft)
        {
            isLeft = obj.name.EndsWith("L");
            return obj.name.Contains("Bone_Pointer_C");
        }
    }
}