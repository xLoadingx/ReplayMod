using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Networking.MatchFlow;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Slabs.Forms;
using Il2CppRUMBLE.Social.Phone;
using Il2CppSystem;
using Il2CppSystem.IO;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using ReplayMod.Replay;
using ReplayMod.Replay.Files;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using RumbleModdingAPI.RMAPI;
using UIFramework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using AudioManager = Il2CppRUMBLE.Managers.AudioManager;
using BuildInfo = ReplayMod.Core.BuildInfo;
using InteractionButton = Il2CppRUMBLE.Interactions.InteractionBase.InteractionButton;
using Main = ReplayMod.Core.Main;
using Random = UnityEngine.Random;
using Utilities = ReplayMod.Replay.Utilities;

[assembly: MelonInfo(typeof(Main), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonColor(255, 255, 0, 0), MelonAuthorColor(255, 255, 0, 0)]
[assembly: MelonAdditionalDependencies("RumbleModdingAPI","UIFramework")]

namespace ReplayMod.Core;

public static class BuildInfo
{
    public const string Name = "ReplayMod";
    public const string Author = "ERROR";
    public const string Version = "1.2.0";
    public const string FormatVersion = "1.1.0";
}

public class Main : MelonMod
{
    // Runtime
    public static Main instance;
    public Main() => instance = this;
    
    public static string currentScene => Calls.Scene.GetSceneName();
    public static bool isSceneReady = false;

    public static ReplayPlayback Playback;
    public static ReplayRecording Recording;

    // Local player
    public static Player LocalPlayer => PlayerManager.instance.localPlayer;
    public Transform leftHand, rightHand, head;
    public string leftShiftstonePool, rightShiftstonePool;

    // Recording FX / timers
    public GameObject clapperboardVFX;
    public bool hasPlayed;
    public float heldTime, soundTimer = 0f;
    public float lastTriggerTime = 0f;

    // UI
    public bool UIInitialized = false;
    
    public ReplayTable replayTable;
    public GameObject flatLandRoot;
    public bool? lastFlatLandActive;

    public ReplaySettings replaySettings;
    public object crystalBreakCoroutine;

    // ------ Settings ------

    public const float errorsArmspan = 1.2744f;
    
    private const string USER_DATA = "UserData/ReplayMod/Settings/";
    private const string CONFIG_FILE = "config.cfg";

    // Recording
    public MelonPreferences_Entry<int> TargetRecordingFPS = new();
    public MelonPreferences_Entry<bool> AutoRecordMatches = new();
    public MelonPreferences_Entry<bool> AutoRecordParks = new();

    public MelonPreferences_Entry<bool> HandFingerRecording = new();
    public MelonPreferences_Entry<bool> CloseHandsOnPose = new();

    // Recording - Voices
    public MelonPreferences_Entry<bool> VoiceRecording = new();
    public MelonPreferences_Entry<int> voiceBitrate = new();

    // Automatic Markers - Match End
    public MelonPreferences_Entry<bool> EnableMatchEndMarker = new();

    // Automatic Markers - Round End
    public MelonPreferences_Entry<bool> EnableRoundEndMarker = new();

    // Automatic Markers - Large Damage
    public MelonPreferences_Entry<bool> EnableLargeDamageMarker;
    public MelonPreferences_Entry<int> DamageThreshold;
    public MelonPreferences_Entry<float> DamageWindow;

    // Playback
    public MelonPreferences_Entry<bool> StopReplayWhenDone;
    public MelonPreferences_Entry<bool> PlaybackControlsFollow;
    public MelonPreferences_Entry<bool> DestroyControlsOnPunch;
    public MelonPreferences_Entry<bool> ToggleUI;

    // Playback Toggles
    public MelonPreferences_Entry<bool> ToggleNameplate;
    public MelonPreferences_Entry<bool> ToggleHealthBar;
    public MelonPreferences_Entry<bool> ToggleDust;
    public MelonPreferences_Entry<bool> ToggleHitmarkers;
    public MelonPreferences_Entry<bool> ToggleRockCam;
    public MelonPreferences_Entry<bool> ToggleVoices;

    // Replay Explorer
    public MelonPreferences_Entry<ReplayExplorer.SortingType> ExplorerSorting;
    public MelonPreferences_Entry<bool> SortingDirection;
    public MelonPreferences_Entry<bool> FavoritesFirst;

    // Replay Buffer
    public MelonPreferences_Entry<bool> ReplayBufferEnabled;
    public MelonPreferences_Entry<int> ReplayBufferDuration;

    // Controls
    public enum ControllerAction
    {
        None,
        [Display(Name = "Toggle Recording")]
        ToggleRecording,
        [Display(Name = "Save Replay Buffer")]
        SaveReplayBuffer,
        [Display(Name = "Add Marker")]
        AddMarker
    }
    
    public MelonPreferences_Entry<ControllerAction> LeftHandControls;
    public MelonPreferences_Entry<ControllerAction> RightHandControls;

    public MelonPreferences_Entry<bool> EnableHaptics;

    // Other
    public MelonPreferences_Entry<float> tableOffset;
    public MelonPreferences_Entry<bool> enableDebug;

    // ------------

    public static void ReplayError(string message = null, Vector3 position = default)
    {
        try
        {
            if (position == default && instance.head != null)
                position = instance.head.position;

            AudioManager.instance.Play(
                ReplayCache.SFX["Call_Measurement_Failure"],
                position
            );
        }
        catch { }

        if (!string.IsNullOrEmpty(message))
            instance.LoggerInstance.Error(message);
    }

    public static void DebugLog(string message)
    {
        if ((instance.enableDebug?.Value ?? false) && !string.IsNullOrEmpty(message))
            instance.LoggerInstance.Warning(message);
    }

    // ----- Init -----

    public override void OnInitializeMelon()
    {
        if (!Directory.Exists(USER_DATA))
            Directory.CreateDirectory(USER_DATA);

        string configPath = Path.Combine(USER_DATA, CONFIG_FILE);

        var recordingFolder = MelonPreferences.CreateCategory("Recording");
        recordingFolder.SetFilePath(configPath);
        
        TargetRecordingFPS = recordingFolder.CreateEntry("Recording_FPS", 50, "Recording FPS", "The target frame rate used when recording replays.\nThis is limited by the game's actual frame rate.");
        AutoRecordMatches = recordingFolder.CreateEntry("Automatically_Record_Matches", true, "Automatically Record Matches", "Automatically start recording when you join a match");
        AutoRecordParks = recordingFolder.CreateEntry("Automatically_Record_Parks", false, "Automatically Record Parks", "Automatically start recording when you join a park");
        
        HandFingerRecording = recordingFolder.CreateEntry("Finger_Animation_Recording", true, "Finger Animation Recording", "Controls whether finger input values are recorded into the replay.");
        CloseHandsOnPose = recordingFolder.CreateEntry("Close_Hands_On_Pose", true, "Close Hands On Pose", "Closes the hands of a clone when they do a pose.");

        var voiceFolder = MelonPreferences.CreateCategory("Voices");
        voiceFolder.SetFilePath(configPath);
        
        VoiceRecording = voiceFolder.CreateEntry("Voice_Recording", true, "Record In-Game Voices", "Toggles whether in-game voices are recorded into replays.");
        voiceBitrate = voiceFolder.CreateEntry("Voice_Bitrate", 30, "Voice Bitrate", "Determines what bitrate voices are recorded in.\nDefault: 30");
        
        var automaticMarkersFolder = MelonPreferences.CreateCategory("Automatic_Markers", "Automatic Markers");
        automaticMarkersFolder.SetFilePath(configPath);
        
        EnableMatchEndMarker = automaticMarkersFolder.CreateEntry("Match_End_Marker", true, "Match End Marker", "Automatically adds a marker at the end of a match.");
        EnableRoundEndMarker = automaticMarkersFolder.CreateEntry("Round_End_Marker", true, "Round End Marker", "Automatically adds a marker at the end of a round.");

        EnableLargeDamageMarker = automaticMarkersFolder.CreateEntry("Large_Damage_Marker", false, "Large Damage Marker", "Automatically adds a marker when a player takes a large amount of damage in a short amount of time.");
        DamageThreshold = automaticMarkersFolder.CreateEntry("Damage_Threshold", 12, "Damage Threshold", "The minimum total damage required to create a marker.");
        DamageWindow = automaticMarkersFolder.CreateEntry("Damage_Window", 1f, "Damage Window (Seconds)", "The time window (in seconds) during which damage is summed to determine whether a marker should be created.");

        var playbackFolder = MelonPreferences.CreateCategory("Playback");
        playbackFolder.SetFilePath(configPath);
        
        StopReplayWhenDone = playbackFolder.CreateEntry("Stop_Replay_On_Finished", false, "Stop Replay On Finished", "Stops a replay when it reaches the end or beginning of its duration.");
        PlaybackControlsFollow = playbackFolder.CreateEntry("Playback_Controls_Follow_Player", false, "Playback Controls Follow Player", "Makes the playback controls menu follow you when opened.");
        DestroyControlsOnPunch = playbackFolder.CreateEntry("Destroy_Controls_On_Punch", true, "Destroy Controls On Punch", "Destroys the playback controls when you punch the slab hard enough.");
        
        var playbackTogglesFolder = MelonPreferences.CreateCategory("Visual Toggles");
        playbackTogglesFolder.SetFilePath(configPath);

        ToggleUI = playbackTogglesFolder.CreateEntry("Toggle_UI", true, "Toggle UI", "Toggles whether the UI for selecting replays is visible.");
        ToggleNameplate = playbackTogglesFolder.CreateEntry("Toggle_Player_Nameplates", false, "Toggle Player Nameplates", "Toggles whether the nameplate on replay clones are visible");
        ToggleHealthBar = playbackTogglesFolder.CreateEntry("Toggle_Player_Healthbars", true, "Toggle Player Healthbars", "Toggles whether the healthbar on replay clones are visible.");
        ToggleDust = playbackTogglesFolder.CreateEntry("Toggle_Dust", true, "Toggle Dust", "Toggles whether dust from replay structures are visible.");
        ToggleHitmarkers = playbackTogglesFolder.CreateEntry("Toggle_Hitmarkers", true, "Toggle Hitmarkers", "Toggles whether hitmarkers (on remote player damage) are visible.");
        ToggleRockCam = playbackTogglesFolder.CreateEntry("Toggle_Rock_Cam", true, "Toggle Rock Cam", "Toggles whether replay clones Rock Cams are visible.");
        ToggleVoices = playbackTogglesFolder.CreateEntry("Toggle_Voices", true, "Toggle Voice Playback", "Toggles whether replay clones playback the recorded speech.");

        var explorerFolder = MelonPreferences.CreateCategory("Replay_Explorer", "Replay Explorer");
        explorerFolder.SetFilePath(configPath);

        ExplorerSorting = explorerFolder.CreateEntry("Sorting_Option", ReplayExplorer.SortingType.Date, "Sorting Option", "The sorting option for the list of replays.");
        SortingDirection = explorerFolder.CreateEntry("Reverse_Sorting", false, "Reverse Sorting", "If enabled, reverses the sort order.");
        FavoritesFirst = explorerFolder.CreateEntry("Favorites_First", true, "Put Favorites First", "Toggles whether favorited replays should always come first in the list.");

        var bufferFolder = MelonPreferences.CreateCategory("Buffer");
        bufferFolder.SetFilePath(configPath);

        ReplayBufferEnabled = bufferFolder.CreateEntry("Enable_Replay_Buffer", false, "Enable Replay Buffer", "Keeps a rolling buffer of recent gameplay that can be saved as a replay.");
        ReplayBufferDuration = bufferFolder.CreateEntry("Replay_Buffer_Duration", 30, "Replay Buffer Duration (seconds)", "How much gameplay time (in seconds) is kept in the replay buffer.");
        
        var controlsFolder = MelonPreferences.CreateCategory("Controls");
        controlsFolder.SetFilePath(configPath);

        LeftHandControls = controlsFolder.CreateEntry("Left_Controller_Binding", ControllerAction.None, "Left Controller Binding",
            "Selects the action performed when both buttons on the left controller are pressed at the same time.");

        RightHandControls = controlsFolder.CreateEntry("Right_Controller_Binding", ControllerAction.None, "Right Controller Binding",
            "Selects the action performed when both buttons on the right controller are pressed at the same time.");

        EnableHaptics = controlsFolder.CreateEntry("Enable_Haptics", true, "Enable Haptics",
            "Plays controller haptics when actions such as saving a replay or adding a marker are performed.");

        if (ReplayAPI.Extensions.Any())
        {
            foreach (var ext in ReplayAPI.Extensions)
            {
                var extFolder = MelonPreferences.CreateCategory($"Extension_{ext.Id}", $"{ext.Id}");
                extFolder.SetFilePath(configPath);

                var toggle = extFolder.CreateEntry("Enabled", true, $"Toggle {ext.Id}", "Toggles the extension on/off");

                ext.Enabled = toggle;
            }
        }
        
        var otherFolder = MelonPreferences.CreateCategory("Other");
        otherFolder.SetFilePath(configPath);

        tableOffset = otherFolder.CreateEntry("Replay_Table_Height_Offset", 0f, "Replay Table Height Offset",
            "Adjusts the vertical offset of the replay table in meters.\nUseful if the table feels too high or too low.");

        enableDebug = otherFolder.CreateEntry("Enable_Debug", false, "Enable Debug",
            "Enables debug logs.\nIf something breaks, turn this on and wait for it to happen again.\nInclude your log when reporting bugs to ERROR.");
        
        ReplayBufferEnabled.OnEntryValueChanged.Subscribe((_, value) =>
        {
            if (value && !Recording.isBuffering)
                Recording.StartBuffering();

            Recording.isBuffering = value;
        });
        
        ExplorerSorting.OnEntryValueChanged.Subscribe((_, _) => ReplayFiles.ReloadReplays());
        SortingDirection.OnEntryValueChanged.Subscribe((_, _) => ReplayFiles.ReloadReplays());
        FavoritesFirst.OnEntryValueChanged.Subscribe((_, _) => ReplayFiles.ReloadReplays());
        
        ToggleNameplate.OnEntryValueChanged.Subscribe((_, value) =>
        {
            if (!Playback?.isPlaying ?? true)
                return;

            foreach (var player in Playback.PlaybackPlayers)
                player.Controller.PlayerNameTag.gameObject.SetActive(value);
        });

        ToggleHealthBar.OnEntryValueChanged.Subscribe((_, value) =>
        {
            if (!Playback?.isPlaying ?? true)
                return;

            foreach (var player in Playback.PlaybackPlayers)
                player.Controller.PlayerHealth.transform.GetChild(1).gameObject.SetActive(value);
        });

        ToggleRockCam.OnEntryValueChanged.Subscribe((_, value) =>
        {
            if (!Playback?.isPlaying ?? true)
                return;

            foreach (var player in Playback.PlaybackPlayers)
                player.Controller.PlayerLIV.LckTablet.gameObject.SetActive(value);
        });
        
        ToggleUI.OnEntryValueChanged.Subscribe((_, value) =>
        {
            replayTable?.gameObject.SetActive(value);
            replayTable?.metadataText?.gameObject.SetActive(value);
        });
        
        VoiceRecording.OnEntryValueChanged.Subscribe((_, value) =>
        {
            if (value && !ReplayVoices.isRecording)
                ReplayVoices.StartRecording();
            else if (!value && ReplayVoices.isRecording)
                ReplayVoices.StopRecording();
        });
        
        ToggleVoices.OnEntryValueChanged.Subscribe((_, value) =>
        {
            if (!Playback.isPlaying)
                return;
            
            if (!value)
            {
                foreach (var clone in Playback.PlaybackPlayers)
                    clone.VoiceSource.Stop();
            }
        });
        
        UI.Register(this,
            recordingFolder,
            automaticMarkersFolder,
            playbackFolder,
            playbackTogglesFolder,
            explorerFolder,
            bufferFolder,
            controlsFolder,
            otherFolder
        );

        UIInitialized = true;
    }

    public override void OnLateInitializeMelon()
    {
        if (!UIInitialized) return;
        
        Actions.onMapInitialized += (scene) => OnMapInitialized();
        
        Actions.onRoundEnded += () =>
        {
            if (!EnableRoundEndMarker.Value)
                return;

            Recording.AddMarker("core.roundEnded", new Color(0.7f, 0.6f, 0.85f));
        };

        Actions.onMatchEnded += () =>
        {
            if (EnableMatchEndMarker.Value)
                Recording.AddMarker("core.matchEnded", Color.black);
        };
        
        ReplayFiles.Init();

        Recording = new();
        Playback = new(Recording);
    }
    
    private IEnumerator ListenForFlatLand()
    {
        yield return new WaitForSeconds(1f);
        flatLandRoot = GameObject.Find("FlatLand");
    }

    public override void OnApplicationQuit()
    {
        if (Recording.isRecording)
            Recording.StopRecording();

        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "ReplayMod", "TempReplayVoices");
        if (System.IO.Directory.Exists(directory))
            System.IO.Directory.Delete(directory, true);
    }
    
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        isSceneReady = false;
        
        if (currentScene == "Loader" || !UIInitialized)
            return;

        DebugLog($"Scene loaded: {sceneName}");
        DebugLog($"Recording active before scene load: {Recording.isRecording}");
        DebugLog($"Playback active before scene load: {Playback.isPlaying}");
        
        if (Recording.isRecording)
            Recording.StopRecording();

        if (Playback.isPlaying)
            Playback.StopReplay();

        if (sceneName == "Gym")
            MelonCoroutines.Start(ListenForFlatLand());

        if (currentScene == "Gym")
            ReplayPlayback.isReplayScene = false;

        Recording.Reset();
    }

    public void OnMapInitialized()
    {
        if (!UIInitialized) return;
        
        var recordingIcon = Create.NewText().GetComponent<TextMeshPro>();
        recordingIcon.transform.SetParent(LocalPlayer.Controller.GetSubsystem<PlayerUI>().transform.GetChild(0));
        recordingIcon.name = "Replay Recording Icon";
        recordingIcon.color = new Color(0, 1, 0, 0);
        recordingIcon.text = "●";
        recordingIcon.ForceMeshUpdate();
        recordingIcon.transform.localPosition = new Vector3(0.2313f, 0.0233f, 0.9604f);
        recordingIcon.transform.localRotation = Quaternion.Euler(20.2549f, 18.8002f, 0);
        recordingIcon.transform.localScale = Vector3.one * 0.4f;

        var recordingIconComp = recordingIcon.gameObject.AddComponent<ReplayRecording.RecordingIcon>();
        recordingIconComp.tmp = recordingIcon;
        recordingIconComp.recording = Recording;
        
        ReplayRecording.recordingIcon = recordingIconComp;
        
        recordingIconComp.SyncToState();
        
        if (((currentScene is "Map0" or "Map1" && AutoRecordMatches.Value && PlayerManager.instance.AllPlayers.Count > 1) || (currentScene == "Park" && AutoRecordParks.Value)) && !ReplayPlayback.isReplayScene)
            Recording.StartRecording();

        if ((ReplayCache.SFX == null || ReplayCache.structurePools == null) && currentScene != "Loader")
            ReplayCache.BuildCacheTables();

        if (replayTable == null && clapperboardVFX == null)
            LoadReplayObjects();

        if (ReplayCrystals.crystalParent == null)
            ReplayCrystals.crystalParent = new GameObject("Crystals");
        
        if (currentScene == "Gym")
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);
            
            if (replayTable.tableFloat != null)
                replayTable.tableFloat.startPos = new Vector3(5.9506f, 1.3564f, 4.1906f);
            
            if (replayTable.metadataTextFloat != null)
                replayTable.metadataTextFloat.startPos = new Vector3(5.9575f, 1.8514f, 4.2102f);
            
            replaySettings.gameObject.transform.localPosition = new Vector3(0.3782f, 0.88f, 0.1564f);
            replaySettings.gameObject.transform.localRotation = Quaternion.Euler(23.4376f, 90f, 90f);
            
            replayTable.tableOffset = 0f;
            replayTable.transform.localRotation = Quaternion.Euler(270, 121.5819f, 0);
        }
        else if (currentScene == "Park")
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);
            
            replayTable.tableFloat.startPos = new Vector3(-28.9436f, -1.5689f, -6.9218f);
            replayTable.metadataTextFloat.startPos = new Vector3(-28.9499f, -2.0639f, -6.9414f);
            
            replaySettings.gameObject.transform.localPosition = new Vector3(0.5855f, -0.9382f, 0.1346f);
            replaySettings.gameObject.transform.localRotation = Quaternion.Euler(325.5624f, 90f, 90f);
            
            replayTable.tableOffset = -3.06f;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 0, 0);

            if (ReplayPlayback.isReplayScene)
                MelonCoroutines.Start(DelayedParkLoad());

        } 
        else if (currentScene == "Map0" && PlayerManager.instance.AllPlayers.Count == 1)
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);

            replayTable.tableFloat.startPos = new Vector3(15.671f, -4.4577f, -8.5671f);
            replayTable.metadataTextFloat.startPos = new Vector3(15.671f, -4.4577f, -8.5671f);
            
            replaySettings.gameObject.transform.localPosition = new Vector3(0.5855f, -0.9382f, 0.1346f);
            replaySettings.gameObject.transform.localRotation = Quaternion.Euler(325.5624f, 90f, 90f);
            
            replayTable.tableOffset = -5.8f;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 215.5305f, 0f);
        } 
        else if (currentScene == "Map1" && PlayerManager.instance.AllPlayers.Count == 1)
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);

            replayTable.tableFloat.startPos = new Vector3(-13.8339f, 1.1872f, -0.3379f);
            replayTable.metadataTextFloat.startPos = new Vector3(-13.8339f, 1.1872f, -0.3379f);
            
            replaySettings.gameObject.transform.localPosition = new Vector3(0.5855f, -0.9382f, 0.1346f);
            replaySettings.gameObject.transform.localRotation = Quaternion.Euler(325.5624f, 90f, 90f);
            
            replayTable.tableOffset = -0.35f;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 352.1489f, 0f);
        }
        else
        {
            replayTable.TableRoot.SetActive(false);
            replayTable.metadataText.gameObject.SetActive(false);
        }
        
        if (ReplayPlayback.isReplayScene)
        {
            MatchHandler matchHandler = currentScene switch
            {
                "Map1" => GameObjects.Map1.Logic.MatchHandler.GetGameObject().GetComponent<MatchHandler>(),
                "Map0" => GameObjects.Map0.Logic.MatchHandler.GetGameObject().GetComponent<MatchHandler>(),
                _ => null
            };

            if (matchHandler != null)
            {
                matchHandler.CurrentMatchPhase = MatchHandler.MatchPhase.MatchStart;
                matchHandler.FadeIn();
            }
        }
        
        string[] spellings = { "Heisenhouser", "Heisenhowser", "Heisenhouwser", "Heisenhouwer" };
        replayTable.heisenhouserText.text = spellings[Random.Range(0, spellings.Length)];
        replayTable.heisenhouserText.ForceMeshUpdate();

        ReplayPlaybackControls.destroyOnPunch.leftHand = LocalPlayer.Controller.GetSubsystem<PlayerHandPresence>().leftInteractionHand;
        ReplayPlaybackControls.destroyOnPunch.rightHand = LocalPlayer.Controller.GetSubsystem<PlayerHandPresence>().rightInteractionHand;
        
        ReplayCrystals.LoadCrystals(currentScene);
        ReplayFiles.LoadReplays();
        ReplayFiles.RefreshUI();
        
        ReplayPlaybackControls.Close();
        
        if (currentScene != "Loader" && ReplayBufferEnabled.Value)
            Recording.StartBuffering();
        
        if (Recording.isRecording || Recording.isBuffering)
            Recording.SetupRecordingData();

        if (Playback.playerPoolRoot == null)
        {
            GameObject playerPoolRoot = new GameObject("[ReplayMod] Player Pool Root");
            GameObject.DontDestroyOnLoad(playerPoolRoot);
            
            Playback.playerPoolRoot = playerPoolRoot;
        }

        var vr = LocalPlayer.Controller.transform.GetChild(2);
        replayTable.metadataText.GetComponent<LookAtPlayer>().lockX = true;
        replayTable.metadataText.GetComponent<LookAtPlayer>().lockZ = true;

        leftHand = vr.GetChild(1);
        rightHand = vr.GetChild(2);
        head = vr.GetChild(0).GetChild(0);

        IEnumerator ShiftstoneApplyDelay()
        {
            yield return new WaitForSeconds(0.5f);
            
            if (PlayerManager.instance.AllPlayers.Count == 1)
            {
                if (!string.IsNullOrEmpty(leftShiftstonePool))
                    LocalPlayer.Controller.PlayerShiftstones.AttachShiftStone(PoolManager.instance.GetPool(leftShiftstonePool).FetchFromPool().GetComponent<ShiftStone>(), 0);
            
                if (!string.IsNullOrEmpty(rightShiftstonePool))
                    LocalPlayer.Controller.PlayerShiftstones.AttachShiftStone(PoolManager.instance.GetPool(rightShiftstonePool).FetchFromPool().GetComponent<ShiftStone>(), 1);

                leftShiftstonePool = null;
                rightShiftstonePool = null;
            }
        }

        MelonCoroutines.Start(ShiftstoneApplyDelay());

        Playback.SetPlaybackSpeed(1f);
        ReplayPlayback.isReplayScene = false;

        isSceneReady = true;
    }

    IEnumerator DelayedParkLoad()
    {
        if (EnableHaptics.Value)
            LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.1f, 1f, 0.1f);
        
        yield return new WaitForSeconds(2f);

        if (currentScene == "Park")
        {
            if (PhotonNetwork.CurrentRoom != null)
                PhotonNetwork.CurrentRoom.isVisible = false;
            
            GameObjects.Park.LOGIC.ParkInstance.GetGameObject().SetActive(false);
            PhotonNetwork.LeaveRoom();
            
            yield return new WaitForSeconds(1f);
        }

        Playback.LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
        SimpleScreenFadeInstance.Progress = 0f;
    }

    IEnumerator DelayedFlatLandLoad()
    {
        if (flatLandRoot == null) yield break;

        while (flatLandRoot.activeSelf)
            yield return null;
        
        yield return new WaitForSeconds(1f);

        Playback.LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
        SimpleScreenFadeInstance.Progress = 0f;
    }

    public void LoadReplayObjects()
    {
        GameObject ReplayTable = new GameObject("Replay Table");

        ReplayTable.transform.localPosition = new Vector3(5.9506f, 1.3564f, 4.1906f);
        ReplayTable.transform.localRotation = Quaternion.Euler(270f, 121.5819f, 0f);

        using var stream = typeof(Main).Assembly.GetManifestResourceStream("ReplayMod.src.Core.replayobjects");
        byte[] bundleData = new byte[stream.Length];
        stream.Read(bundleData, 0, bundleData.Length);

        AssetBundle bundle = AssetBundle.LoadFromMemory(bundleData);
        
        GameObject table = GameObject.Instantiate(bundle.LoadAsset<GameObject>("Table"), ReplayTable.transform);

        table.name = "Table";
        table.transform.localScale *= 0.5f;
        table.transform.localRotation = Quaternion.identity;

        Material tableMat = new Material(GameObjects.Gym.INTERACTABLES.Gearmarket.Stallframe.GetGameObject().GetComponent<Renderer>().material);
        tableMat.SetTexture("_Albedo", bundle.LoadAsset<Texture2D>("Texture"));
        tableMat.SetTexture("_Lighting_data", null);
        table.GetComponent<Renderer>().material = tableMat;

        var tableFloat = ReplayTable.AddComponent<TableFloat>();
        tableFloat.speed = (2 * PI) / 10f;
        tableFloat.amplitude = 0.01f;

        table.layer = LayerMask.NameToLayer("LeanableEnvironment");
        table.AddComponent<MeshCollider>();

        if (Calls.Mods.findOwnMod("Rumble Dark Mode", "Bleh", false))
            table.GetComponent<Renderer>().lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        GameObject levitateVFX = GameObject.Instantiate(
            GameObjects.Gym.INTERACTABLES.Parkboard.PlayerRelocationTrigger.StandHere.GetGameObject(),
            ReplayTable.transform
        );

        levitateVFX.name = "LevitateVFX";
        levitateVFX.transform.localPosition = new Vector3(0, 0, -0.2764f);
        levitateVFX.transform.localRotation = Quaternion.Euler(270, 0, 0);
        levitateVFX.transform.localScale = Vector3.one * 0.8f;

        GameObject Next = GameObject.Instantiate(
            GameObjects.Gym.INTERACTABLES.Telephone20REDUXspecialedition.FriendScreen.FriendScrollBar.ScrollUpButton.GetGameObject(),
            ReplayTable.transform
        );

        Next.name = "Next Replay";
        Next.transform.localPosition = new Vector3(0.2978f, -0.248f, -0.181f);
        Next.transform.localRotation = Quaternion.Euler(345.219f, 340.1203f, 234.8708f);
        Next.transform.localScale = Vector3.one * 1.8f;
        var nextButton = Next.transform.GetChild(0).GetComponent<InteractionButton>();
        nextButton.enabled = true;
        nextButton.onPressedAudioCall = ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"];
        nextButton.OnPressed.RemoveAllListeners();
        nextButton.OnPressed.AddListener((UnityAction)(() => { ReplayFiles.NextReplay(); }));

        GameObject Previous = GameObject.Instantiate(Next, ReplayTable.transform);

        Previous.name = "Previous Replay";
        Previous.transform.localPosition = new Vector3(0.3204f, 0.2192f, -0.1844f);
        Previous.transform.localRotation = Quaternion.Euler(10.6506f, 337.4582f, 296.0434f);
        Previous.transform.GetChild(0).GetChild(3).localRotation = Quaternion.Euler(90, 180, 0);
        var previousButton = Previous.transform.GetChild(0).GetComponent<InteractionButton>();
        previousButton.enabled = true;
        previousButton.onPressedAudioCall = ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardUnlocked"];
        previousButton.OnPressed.RemoveAllListeners();
        previousButton.OnPressed.AddListener((UnityAction)(() => { ReplayFiles.PreviousReplay(); }));

        var replayNameText = Create.NewText("No Replay Selected", 5f, new Color(0.102f, 0.051f, 0.0275f), Vector3.zero, Quaternion.identity);
        replayNameText.transform.SetParent(ReplayTable.transform);

        replayNameText.name = "Replay Name";
        replayNameText.transform.localScale = Vector3.one * 0.1f;
        replayNameText.transform.localPosition = new Vector3(0.446f, -0.0204f, -0.1297f);
        replayNameText.transform.localRotation = Quaternion.Euler(0, 249.3179f, 270f);

        var replayNameTextComp = replayNameText.GetComponent<TextMeshPro>();
        replayNameTextComp.fontSizeMin = 2f;
        replayNameTextComp.fontSizeMax = 2.55f;
        replayNameTextComp.enableAutoSizing = true;
        replayNameTextComp.enableWordWrapping = true;
        replayNameTextComp.GetComponent<RectTransform>().sizeDelta = new Vector2(3, 0.7f);

        var indexText = Create.NewText("(0 / 0)", 5f, new Color(0.102f, 0.051f, 0.0275f), Vector3.zero, Quaternion.identity);
        indexText.transform.SetParent(ReplayTable.transform);

        indexText.name = "Replay Index";
        indexText.transform.localScale = Vector3.one * 0.04f;
        indexText.transform.localPosition = new Vector3(0.4604f, -0.0204f, -0.1697f);
        indexText.transform.localRotation = Quaternion.Euler(0, 249.3179f, 270f);

        var metadataText = Create.NewText("", 5f, Color.white, Vector3.zero, Quaternion.identity);

        metadataText.name = "Metadata Text";
        metadataText.transform.position = new Vector3(5.9575f, 1.8514f, 4.2102f);
        metadataText.transform.localScale = Vector3.one * 0.25f;

        var textTableFloat = metadataText.AddComponent<TableFloat>();
        textTableFloat.speed = (2.5f * PI) / 10;
        textTableFloat.amplitude = 0.01f;
        textTableFloat.stopRadius = 0f;

        var metadataTMP = metadataText.GetComponent<TextMeshPro>();
        metadataTMP.m_HorizontalAlignment = HorizontalAlignmentOptions.Center;

        var lookAt = metadataText.AddComponent<LookAtPlayer>();
        lookAt.lockX = true;
        lookAt.lockZ = true;

        GameObject.DontDestroyOnLoad(metadataText);

        var loadReplayButton = GameObject.Instantiate(GameObjects.Gym.INTERACTABLES.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.ResetFighterButton.GetGameObject(), ReplayTable.transform);

        loadReplayButton.name = "Load Replay";
        loadReplayButton.transform.localPosition = new Vector3(0.5267f, -0.0131f, -0.1956f);
        loadReplayButton.transform.localRotation = Quaternion.Euler(0, 198.4543f, 0);
        loadReplayButton.transform.localScale = new Vector3(0.5129f, 1.1866f, 0.78f);
        loadReplayButton.transform.GetChild(1).transform.localScale = new Vector3(0.23f, 0.1f, 0.1f);

        var loadReplayButtonComp = loadReplayButton.transform.GetChild(0).GetComponent<InteractionButton>();
        loadReplayButtonComp.enabled = true;
        loadReplayButtonComp.longPressTime = 0.25f;
        loadReplayButtonComp.OnPressed.RemoveAllListeners();

        loadReplayButtonComp.onPressedAudioCall = loadReplayButtonComp.longPressAudioCall;

        replayTable = ReplayTable.AddComponent<ReplayTable>();
        replayTable.TableRoot = ReplayTable;

        replayTable.nextButton = nextButton;
        replayTable.previousButton = previousButton;
        replayTable.loadButton = loadReplayButtonComp;

        replayTable.replayNameText = replayNameText.GetComponent<TextMeshPro>();
        replayTable.indexText = indexText.GetComponent<TextMeshPro>();
        replayTable.metadataText = metadataTMP;

        loadReplayButtonComp.OnPressed.AddListener((UnityAction)(() =>
        {
            LoadSelectedReplay();
        }));

        ReplayFiles.table = replayTable;
        ReplayFiles.HideMetadata();

        clapperboardVFX = GameObject.Instantiate(bundle.LoadAsset<GameObject>("Clapper"));
        clapperboardVFX.name = "ClapperboardVFX";
        clapperboardVFX.transform.localScale = Vector3.one * 5f;

        Material clapperboardMat = new Material(tableMat);
        clapperboardMat.SetTexture("_Albedo", bundle.LoadAsset<Texture2D>("ClapperTexture"));
        clapperboardVFX.GetComponent<Renderer>().material = clapperboardMat;
        clapperboardVFX.transform.GetChild(0).GetComponent<Renderer>().material = clapperboardMat;
        clapperboardVFX.SetActive(false);

        GameObject vfx = GameObject.Instantiate(PoolManager.instance.GetPool("Stubbornstone_VFX").poolItem.gameObject, ReplayTable.transform);

        vfx.name = "Crystalize VFX";
        vfx.transform.localScale = Vector3.one * 0.2f;
        vfx.transform.localPosition = new Vector3(0, 0, 0.3903f);
        vfx.SetActive(true);

        VisualEffect vfxComp = vfx.GetComponent<VisualEffect>();
        vfxComp.playRate = 0.6f;

        ReplayCrystals.crystalizeVFX = vfxComp;

        var crystalizeButton = GameObject.Instantiate(loadReplayButton, ReplayTable.transform);

        crystalizeButton.name = "CrystalizeReplay";
        crystalizeButton.transform.localPosition = new Vector3(0.21f, -0.4484f, -0.1325f);
        crystalizeButton.transform.localScale = Vector3.one * 1.1f;
        crystalizeButton.transform.localRotation = Quaternion.Euler(303.8364f, 249f, 108.4483f);

        crystalizeButton.transform.GetChild(1).transform.localScale = Vector3.one * 0.1f;

        var crystalizeButtonComp = crystalizeButton.transform.GetChild(0).GetComponent<InteractionButton>();
        crystalizeButtonComp.enabled = true;
        crystalizeButtonComp.longPressTime = 0.2f;
        crystalizeButtonComp.OnPressed.RemoveAllListeners();

        replayTable.crystalizeButton = crystalizeButtonComp;

        GameObject crystalPrefab = GameObject.Instantiate(bundle.LoadAsset<GameObject>("Crystal"));

        crystalPrefab.transform.localScale *= 0.5f;

        var shiftstoneMat = PoolManager.instance.GetPool("FlowStone").PoolItem.transform.GetChild(0).GetComponent<Renderer>().material;
        foreach (var rend in crystalPrefab.GetComponentsInChildren<Renderer>(true))
            rend.material = shiftstoneMat;

        crystalPrefab.SetActive(false);
        crystalPrefab.transform.localRotation = Quaternion.Euler(-90, 0, 0);

        ReplayCrystals.crystalPrefab = crystalPrefab;

        var crystalizeIcon = new GameObject("CrystalizeIcon");
        crystalizeIcon.transform.SetParent(crystalizeButton.transform.GetChild(0));
        crystalizeIcon.transform.localPosition = new Vector3(0, 0.012f, 0);
        crystalizeIcon.transform.localRotation = Quaternion.Euler(270, 0, 0);
        crystalizeIcon.transform.localScale = Vector3.one * 0.07f;

        var srC = crystalizeIcon.AddComponent<SpriteRenderer>();
        var textureC = bundle.LoadAsset<Texture2D>("CrystalSprite");
        srC.sprite = Sprite.Create(
            textureC,
            new Rect(0, 0, textureC.width, textureC.height),
            new Vector2(0.5f, 0.5f),
            100f
        );

        var heisenhouserIcon = new GameObject("HeisenhouwserIcon");
        heisenhouserIcon.transform.SetParent(ReplayTable.transform);
        heisenhouserIcon.transform.localPosition = new Vector3(-0.2847f, 0.3901f, -0.1354f);
        heisenhouserIcon.transform.localRotation = Quaternion.Euler(51.2548f, 110.7607f, 107.2314f);
        heisenhouserIcon.transform.localScale = Vector3.one * 0.02f;

        var srH = heisenhouserIcon.AddComponent<SpriteRenderer>();
        var textureH = bundle.LoadAsset<Texture2D>("HeisenhowerSprite");
        srH.sprite = Sprite.Create(
            textureH,
            new Rect(0, 0, textureH.width, textureH.height),
            new Vector3(0.5f, 0.5f),
            100f
        );

        GameObject heisenhowuserText = Create.NewText("Heisenhouwser", 1f, Color.white, Vector3.zero, Quaternion.identity);

        heisenhowuserText.transform.SetParent(ReplayTable.transform);
        heisenhowuserText.name = "HeisenhouswerLogoText";

        heisenhowuserText.transform.localPosition = new Vector3(-0.2891f, 0.3969f, -0.1684f);
        heisenhowuserText.transform.localScale = Vector3.one * 0.0035f;
        heisenhowuserText.transform.localRotation = Quaternion.Euler(51.2551f, 110.4334f, 107.2313f);

        replayTable.heisenhouserText = heisenhowuserText.GetComponent<TextMeshPro>();

        replayTable.heisenhouserText.fontSizeMin = 1;
        replayTable.heisenhouserText.enableAutoSizing = true;

        crystalizeButtonComp.onPressedAudioCall = loadReplayButtonComp.onPressedAudioCall;

        ReplayCrystals.crystalParent = new GameObject("Crystals");

        crystalizeButtonComp.OnPressed.AddListener((UnityAction)(() =>
        {
            if (!ReplayCrystals.Crystals.Any(c => c != null && c.ReplayPath == ReplayFiles.explorer.CurrentReplayPath) && ReplayFiles.currentHeader != null && ReplayFiles.explorer.currentIndex != -1)
            {
                AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_Bake_Part"], crystalizeButton.transform.position);

                var header = ReplayFiles.currentHeader;
                ReplayCrystals.CreateCrystal(replayTable.transform.position + new Vector3(0, 0.3f, 0), header, ReplayFiles.explorer.CurrentReplayPath, true);
            }
            else
            {
                ReplayError();
            }
        }));

        var loadReplaySprite = new GameObject("LoadReplaySprite");

        loadReplaySprite.transform.SetParent(loadReplayButtonComp.transform);

        loadReplaySprite.transform.localPosition = new Vector3(0, 0.012f, 0);
        loadReplaySprite.transform.localRotation = Quaternion.Euler(270, 90, 0);
        loadReplaySprite.transform.localScale = new Vector3(0.025f, 0.05f, 0.05f);

        var nextButtonRenderer = nextButton.transform.GetChild(3).GetComponent<MeshRenderer>();
        var nextButtonFilter = nextButton.transform.GetChild(3).GetComponent<MeshFilter>();

        var loadReplayFilter = loadReplaySprite.AddComponent<MeshFilter>();
        var loadReplayRenderer = loadReplaySprite.AddComponent<MeshRenderer>();

        loadReplayFilter.sharedMesh = nextButtonFilter.sharedMesh;
        loadReplayRenderer.material = nextButtonRenderer.material;

        // Playback Controls

        var playbackControls = GameObject.Instantiate(GameObjects.Gym.INTERACTABLES.Notifications.NotificationSlabOther.NotificationSlab.SlabbuddyInfovariant.InfoForm.GetGameObject());
        playbackControls.name = "Playback Controls";
        playbackControls.transform.localScale = Vector3.one;

        playbackControls.transform.GetChild(1).transform.localRotation = Quaternion.Euler(0, 180, 0);
        playbackControls.transform.GetChild(1).transform.localScale = Vector3.one * 0.7f;
        playbackControls.transform.GetChild(1).transform.localPosition = new Vector3(0.0385f, 0.0913f, 0);
        GameObject.Destroy(playbackControls.GetComponent<DisposableObject>());

        GameObject destroyOnPunch = new GameObject("DestroyOnPunch");
        destroyOnPunch.layer = LayerMask.NameToLayer("InteractionBase");
        destroyOnPunch.transform.SetParent(playbackControls.transform);

        var destroyOnPunchComp = destroyOnPunch.AddComponent<DestroyOnPunch>();
        destroyOnPunchComp.onDestroy += ReplayPlaybackControls.Close;

        var boxCollider = destroyOnPunch.AddComponent<BoxCollider>();
        var playbackRenderer = playbackControls.transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>();
        boxCollider.center = playbackRenderer.localBounds.center;
        boxCollider.size = playbackRenderer.localBounds.size;

        GameObject.Destroy(playbackControls.transform.GetChild(2).gameObject);

        for (int i = 0; i < playbackControls.transform.GetChild(1).childCount; i++)
            GameObject.Destroy(playbackControls.transform.GetChild(1).GetChild(i).gameObject);

        var timeline = GameObject.Instantiate(GameObjects.Gym.INTERACTABLES.Gearmarket.Itemhighlightwindow.StatusBar.GetGameObject(),
            playbackControls.transform.GetChild(1));
        Material timelineMaterial = timeline.GetComponent<MeshRenderer>().material;
        timelineMaterial.SetFloat("_Has_BP_Requirement", 1f);
        timelineMaterial.SetFloat("_Has_RC_Requirement", 0f);

        timeline.name = "Timeline";
        timeline.transform.localPosition = new Vector3(0, -0.2044f, -0.0109f);
        timeline.transform.localScale = new Vector3(0.806f, 0.0434f, 1);
        timeline.transform.localRotation = Quaternion.identity;
        timeline.SetActive(true);

        var colliderObj = new GameObject("TimelineCollider");
        colliderObj.transform.SetParent(timeline.transform, false);
        colliderObj.layer = LayerMask.NameToLayer("InteractionBase");

        var col = colliderObj.AddComponent<BoxCollider>();
        col.center = timeline.GetComponent<MeshRenderer>().localBounds.center;
        col.size = timeline.GetComponent<MeshRenderer>().localBounds.size;

        colliderObj.AddComponent<ReplaySettings.TimelineScrubber>();

        var currentDuration = Create.NewText(":3", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
        var totalDuration = Create.NewText(":3", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
        var playbackTitle = Create.NewText("Colon Three", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();
        var playbackSpeedText = Create.NewText("Vewy Fast!", 1f, Color.white, Vector3.zero, Quaternion.identity).GetComponent<TextMeshPro>();

        currentDuration.transform.SetParent(playbackControls.transform.GetChild(1));
        currentDuration.name = "Current Duration";

        currentDuration.transform.localScale = Vector3.one * 0.7f;
        currentDuration.transform.localPosition = new Vector3(-0.3608f, -0.1456f, -0.0062f);
        currentDuration.transform.localRotation = Quaternion.identity;
        currentDuration.enableWordWrapping = false;

        totalDuration.transform.SetParent(playbackControls.transform.GetChild(1));
        totalDuration.name = "Total Duration";

        totalDuration.transform.localScale = Vector3.one * 0.7f;
        totalDuration.transform.localPosition = new Vector3(0.3728f, -0.1456f, -0.0062f);
        totalDuration.transform.localRotation = Quaternion.identity;
        totalDuration.enableWordWrapping = false;

        playbackTitle.transform.SetParent(playbackControls.transform.GetChild(1));
        playbackTitle.name = "Playback Title";
        playbackTitle.transform.localScale = Vector3.one * 0.5f;
        playbackTitle.transform.localPosition = new Vector3(0, 0.3459f, -0.0073f);
        playbackTitle.transform.localRotation = Quaternion.identity;
        playbackTitle.enableWordWrapping = true;
        playbackTitle.enableAutoSizing = true;
        playbackTitle.fontSizeMax = 2f;
        playbackTitle.fontSizeMin = 0.6f;
        playbackTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(1.2f, 0.1577f);
        playbackTitle.alignment = TextAlignmentOptions.Top;
        playbackTitle.horizontalAlignment = HorizontalAlignmentOptions.Center;

        playbackSpeedText.transform.SetParent(playbackControls.transform.GetChild(1));
        playbackSpeedText.name = "Playback Speed";

        playbackSpeedText.transform.localScale = Vector3.one * 0.8f;
        playbackSpeedText.transform.localPosition = new Vector3(0, -0.1278f, -0.0127f);
        playbackSpeedText.transform.localRotation = Quaternion.identity;
        playbackSpeedText.horizontalAlignment = HorizontalAlignmentOptions.Center;
        playbackSpeedText.GetComponent<RectTransform>().sizeDelta = new Vector2(1.2f, 0.1577f);
        playbackSpeedText.fontSize = 1f;

        var friendScrollBar = GameObjects.Gym.INTERACTABLES.Telephone20REDUXspecialedition.FriendScreen.FriendScrollBar.GetGameObject();

        var p5x = GameObject.Instantiate(friendScrollBar.transform.GetChild(0).gameObject, playbackControls.transform.GetChild(1));
        var np5x = GameObject.Instantiate(friendScrollBar.transform.GetChild(1).gameObject, playbackControls.transform.GetChild(1));
        var p1x = GameObject.Instantiate(friendScrollBar.transform.GetChild(2).gameObject, playbackControls.transform.GetChild(1));
        var np1x = GameObject.Instantiate(friendScrollBar.transform.GetChild(3).gameObject, playbackControls.transform.GetChild(1));
        var playButton = GameObject.Instantiate(friendScrollBar.transform.GetChild(0).gameObject, playbackControls.transform.GetChild(1));

        var speedUpTexture = bundle.LoadAsset<Texture2D>("SpeedUp");

        var compp5x = p5x.transform.GetChild(0).GetComponent<InteractionButton>();
        compp5x.enabled = true;
        compp5x.onPressed.RemoveAllListeners();
        compp5x.onPressed.AddListener((UnityAction)(() => { Playback.AddPlaybackSpeed(0.1f); }));
        compp5x.transform.GetChild(3).GetComponent<MeshRenderer>().material.SetTexture("_Texture", speedUpTexture);

        p5x.name = "+0.1 Speed";
        p5x.transform.localScale = Vector3.one * 1.8f;
        p5x.transform.localPosition = new Vector3(0.1598f, -0.3469f, 0.096f);
        p5x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compnp5x = np5x.transform.GetChild(0).GetComponent<InteractionButton>();
        compnp5x.enabled = true;
        compnp5x.onPressed.RemoveAllListeners();
        compnp5x.onPressed.AddListener((UnityAction)(() => { Playback.AddPlaybackSpeed(-0.1f); }));
        compnp5x.transform.GetChild(3).GetComponent<MeshRenderer>().material.SetTexture("_Texture", speedUpTexture);

        np5x.name = "-0.1 Speed";
        np5x.transform.localScale = Vector3.one * 1.8f;
        np5x.transform.localPosition = new Vector3(-0.1598f, -0.3469f, 0.096f);
        np5x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compp1x = p1x.transform.GetChild(0).GetComponent<InteractionButton>();
        compp1x.enabled = true;
        compp1x.onPressed.RemoveAllListeners();
        compp1x.onPressed.AddListener((UnityAction)(() => { Playback.AddPlaybackSpeed(1f); }));

        p1x.name = "+1 Speed";
        p1x.transform.localScale = Vector3.one * 1.8f;
        p1x.transform.localPosition = new Vector3(0.31f, -0.3469f, 0.096f);
        p1x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compnp1x = np1x.transform.GetChild(0).GetComponent<InteractionButton>();
        compnp1x.enabled = true;
        compnp1x.onPressed.RemoveAllListeners();
        compnp1x.onPressed.AddListener((UnityAction)(() => { Playback.AddPlaybackSpeed(-1f); }));

        np1x.name = "-1 Speed";
        np1x.transform.localScale = Vector3.one * 1.8f;
        np1x.transform.localPosition = new Vector3(-0.31f, -0.3469f, 0.096f);
        np1x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compplay = playButton.transform.GetChild(0).GetComponent<InteractionButton>();
        compplay.enabled = true;
        compplay.onPressed.RemoveAllListeners();
        
        ReplayPlaybackControls.playButtonSprite = compplay.transform.GetChild(3)
            .GetComponent<MeshRenderer>();
        
        compplay.onPressed.AddListener((UnityAction)(() => { Playback.TogglePlayback(Playback.isPaused); }));

        playButton.name = "Play Button";
        playButton.transform.localScale = Vector3.one * 2f;
        playButton.transform.localPosition = new Vector3(0, -0.3469f, 0.1156f);
        playButton.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var textureP = bundle.LoadAsset<Texture2D>("Pause");
        ReplayPlaybackControls.pauseSprite = textureP;

        var texturePlay = bundle.LoadAsset<Texture2D>("Play");
        ReplayPlaybackControls.playSprite = texturePlay;

        ReplayPlaybackControls.playButtonSprite.material.SetTexture("_Texture", ReplayPlaybackControls.pauseSprite);
        ReplayPlaybackControls.playButtonSprite.transform.localScale = Vector3.one * 0.07f;

        var stopReplayButton = GameObject.Instantiate(GameObjects.Gym.INTERACTABLES.DressingRoom.Controlpanel.Controls.Frameattachment.RotationOptions.ResetRotationButton.GetGameObject(),
            playbackControls.transform.GetChild(1));

        var exitSceneButton = GameObject.Instantiate(stopReplayButton, playbackControls.transform.GetChild(1));

        stopReplayButton.name = "Stop Replay";
        stopReplayButton.transform.localPosition = new Vector3(-0.1527f, -0.6109f, 0f);
        stopReplayButton.transform.localScale = Vector3.one * 2f;
        stopReplayButton.transform.localRotation = Quaternion.identity;

        var stopReplayComp = stopReplayButton.transform.GetChild(0).GetComponent<InteractionButton>();
        stopReplayComp.enabled = true;
        stopReplayComp.onPressed.RemoveAllListeners();
        stopReplayComp.onPressed.AddListener((UnityAction)(() => { Playback.StopReplay(); }));

        var stopReplayTMP = stopReplayButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        stopReplayTMP.text = "Stop Replay";
        stopReplayTMP.color = new Color(0.8f, 0, 0);
        stopReplayTMP.ForceMeshUpdate();
        stopReplayTMP.gameObject.SetActive(true);

        exitSceneButton.name = "Exit Map";
        exitSceneButton.transform.localPosition = new Vector3(0.1527f, -0.6109f, 0f);
        exitSceneButton.transform.localScale = Vector3.one * 2f;
        exitSceneButton.transform.localRotation = Quaternion.identity;

        var exitSceneComp = exitSceneButton.transform.GetChild(0).GetComponent<InteractionButton>();
        exitSceneComp.enabled = true;
        exitSceneComp.onPressed.RemoveAllListeners();
        exitSceneComp.onPressed.AddListener((UnityAction)(() => { MelonCoroutines.Start(Utilities.LoadMap(1)); }));

        var exitSceneTMP = exitSceneButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        exitSceneTMP.text = "Exit Map";
        exitSceneTMP.color = new Color(0.8f, 0, 0);
        exitSceneTMP.ForceMeshUpdate();
        exitSceneTMP.gameObject.SetActive(true);

        var markerPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        markerPrefab.name = "ReplayMarker";
        markerPrefab.SetActive(false);
        
        var markerRenderer = markerPrefab.GetComponent<MeshRenderer>();
        markerRenderer.material = new Material(Shader.Find("Shader Graphs/RUMBLE_Prop"));

        ReplayPlaybackControls.playbackControls = playbackControls;
        ReplayPlaybackControls.markerPrefab = markerPrefab;
        ReplayPlaybackControls.timeline = timeline;
        ReplayPlaybackControls.currentDuration = currentDuration;
        ReplayPlaybackControls.totalDuration = totalDuration;
        ReplayPlaybackControls.playbackSpeedText = playbackSpeedText;
        ReplayPlaybackControls.playbackTitle = playbackTitle;
        ReplayPlaybackControls.destroyOnPunch = destroyOnPunchComp;
        
        // Replay Settings
        var replaySettingsPanel = GameObject.Instantiate(playbackControls, ReplayTable.transform);
        replaySettingsPanel.SetActive(true);
        GameObject.Destroy(replaySettingsPanel.transform.GetChild(6).gameObject);
        replaySettingsPanel.name = "Replay Settings";
        replaySettingsPanel.transform.localScale = Vector3.one;
        replaySettingsPanel.transform.GetChild(0).GetChild(0).gameObject.layer = LayerMask.NameToLayer("Default");
        GameObject.Destroy(replaySettingsPanel.GetComponent<DisposableObject>());

        var replaySettingsGO = new GameObject("Replay Settings");
        replaySettingsGO.SetActive(false);
        replaySettingsGO.transform.SetParent(replaySettingsPanel.transform.GetChild(0), false);
        
        replaySettingsPanel.transform.GetChild(0).transform.localRotation = Quaternion.Euler(0, 180, 0);
        replaySettingsPanel.transform.GetChild(0).GetChild(0).transform.localRotation = Quaternion.Euler(0, 180, 0);
        
        for (int i = 0; i < replaySettingsPanel.transform.GetChild(1).childCount; i++)
            GameObject.Destroy(replaySettingsPanel.transform.GetChild(1).GetChild(i).gameObject);
        
        replaySettingsPanel.transform.localPosition = new Vector3(0.3782f, 0.88f, 0.1564f);
        replaySettingsPanel.transform.localRotation = Quaternion.Euler(34.4376f, 90, 90);
        
        var povCameraButton = GameObject.Instantiate(
            GameObjects.Gym.INTERACTABLES.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.ResetFighterButton.GetGameObject(),
            replaySettingsGO.transform
        );
        
        povCameraButton.name = "POV Button";
        povCameraButton.transform.localPosition = new Vector3(-0.2341f, 0.1793f, -0.0552f);
        povCameraButton.transform.localRotation = Quaternion.identity;
        povCameraButton.transform.localScale = Vector3.one * 1.1f;
        
        var povCameraButtonComp = povCameraButton.transform.GetChild(0).GetComponent<InteractionButton>();
        povCameraButtonComp.enabled = true;
        povCameraButtonComp.useLongPress = false;
        povCameraButtonComp.onPressedAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Press"];
        povCameraButtonComp.onPressed.RemoveAllListeners();
        povCameraButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            if (GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<Camera>().enabled)
            {
                MelonCoroutines.Start(ReplaySettings.SelectPlayer(selectedPlayer =>
                {
                    Playback.UpdateReplayCameraPOV(selectedPlayer, ReplaySettings.hideLocalPlayer);
                }, 0.5f));
            }
            else
            {
                ReplayError("Legacy cam must be enabled to use the POV feature.");
            }
        }));
        
        var hideLocalPlayerButton = GameObject.Instantiate(
            GameObjects.Gym.INTERACTABLES.DressingRoom.Controlpanel.Controls.Frameattachment.RotationOptions.ResetRotationButton.GetGameObject(), 
            replaySettingsGO.transform
        );
        
        hideLocalPlayerButton.name = "Hide Local Player Toggle";
        hideLocalPlayerButton.transform.localPosition = new Vector3(-0.2195f, 0.0764f, -0.0483f);
        hideLocalPlayerButton.transform.localRotation = Quaternion.identity;
        hideLocalPlayerButton.transform.localScale = Vector3.one * 0.85f;
        
        var hideLocalPlayerTMP =  hideLocalPlayerButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        hideLocalPlayerTMP.text = "Hide Local Player";
        hideLocalPlayerTMP.transform.localScale = Vector3.one * 0.7f;
        hideLocalPlayerTMP.gameObject.SetActive(true);
        
        var hideLocalPlayerComp = hideLocalPlayerButton.transform.GetChild(0).GetComponent<InteractionButton>();
        hideLocalPlayerComp.enabled = true;
        hideLocalPlayerComp.isToggleButton = true;
        hideLocalPlayerComp.onToggleFalseAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Unpress"];
        hideLocalPlayerComp.onToggleTrueAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Press"];
        hideLocalPlayerComp.onPressed.RemoveAllListeners();
        hideLocalPlayerComp.onToggleStateChanged.AddListener((UnityAction<bool>)(toggle =>
        {
            ReplaySettings.hideLocalPlayer = toggle;
            hideLocalPlayerTMP.color = toggle ? Color.green : Color.red;
        
            if (ReplayPlayback.povPlayer != null)
                Playback.UpdateReplayCameraPOV(ReplayPlayback.povPlayer, toggle);
        }));
        hideLocalPlayerComp.SetButtonToggleStatus(true, false, true);
        
        var povIconObj = new GameObject("Player Icon");
        povIconObj.transform.SetParent(povCameraButton.transform.GetChild(0));
        povIconObj.transform.localPosition = new Vector3(0.0008f, 0.012f, -0.0039f);
        povIconObj.transform.localRotation = Quaternion.Euler(90, 0, 0);
        povIconObj.transform.localScale = Vector3.one * 0.06f;
        
        var povIconTexture = bundle.LoadAsset<Texture2D>("POVIcon");
        povIconObj.AddComponent<SpriteRenderer>().sprite = Sprite.Create(
            povIconTexture,
            new Rect(0, 0, povIconTexture.width, povIconTexture.height),
            new Vector3(0.5f, 0.5f),
            100f
        );
        
        var openControlsButton = GameObject.Instantiate(
            GameObjects.Gym.INTERACTABLES.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.ResetFighterButton.GetGameObject(),
            replaySettingsGO.transform
        );
        
        openControlsButton.name = "Open Controls Toggle";
        openControlsButton.transform.localPosition = new Vector3(0.18f, 0.07f, -0.0502f);
        openControlsButton.transform.localRotation = Quaternion.Euler(0, 0, 90);
        openControlsButton.transform.localScale = Vector3.one * 1.1f;
        
        var openControlsComp = openControlsButton.transform.GetChild(0).GetComponent<InteractionButton>();
        openControlsComp.enabled = true;
        openControlsComp.useLongPress = false;
        openControlsComp.onPressed.RemoveAllListeners();
        openControlsComp.onPressed.AddListener((UnityAction)(() =>
        {
            if (ReplayPlaybackControls.playbackControlsOpen) 
                ReplayPlaybackControls.Close();
            else
            {
                ReplayPlaybackControls.Open();

                Vector3 position = currentScene switch
                {
                    "Gym" => new Vector3(4.2525f, 1.5936f, 4.2636f),
                    "Map0" => new Vector3(13.8832f, -4.2314f, -9.1586f),
                    "Map1" => new Vector3(-12.846f, 1.2404f, 1.294f),
                    "Park" => new Vector3(-27.7917f, -1.4615f, -5.4588f),
                    _ => Vector3.zero
                };

                Quaternion rotation = currentScene switch
                {
                    "Gym" => Quaternion.Euler(0, 162.7955f, 0),
                    "Map0" => Quaternion.Euler(0, 3.7954f, 0),
                    "Map1" or "Park" => Quaternion.Euler(0, 143.5775f, 0),
                    _ => Quaternion.identity
                };

                ReplayPlaybackControls.playbackControls.transform.position = position;
                ReplayPlaybackControls.playbackControls.transform.rotation = rotation;
            }
                
        }));
        
        var openControlsText = Create.NewText().GetComponent<TextMeshPro>();
        openControlsText.name = "Open Controls Text";
        openControlsText.transform.SetParent(openControlsComp.transform);
        openControlsText.transform.localPosition = new Vector3(0.0116f, 0.0117f, 0);
        openControlsText.transform.localRotation = Quaternion.Euler(90, 90, 0);
        openControlsText.transform.localScale = Vector3.one * 0.4f;
        
        openControlsText.text = ". . .";
        openControlsText.color = Color.white;
        openControlsText.enableWordWrapping = false;
        openControlsText.fontSizeMax = 1.3f;
        openControlsText.ForceMeshUpdate();
        
        var slideOutPanel = GameObject.Instantiate(bundle.LoadAsset<GameObject>("SlideOutPlayerSelector"), replaySettingsGO.transform);
        
        slideOutPanel.name = "Player Selector Panel";
        slideOutPanel.transform.localScale = Vector3.one * 1.5f;
        slideOutPanel.transform.localPosition = new Vector3(-0.0778f, 0.3898f, 0.0134f);
        slideOutPanel.SetActive(false);
        
        var slideOutText = Create.NewText();
        
        slideOutText.name = "PlayerSelectorNameText";
        slideOutText.transform.SetParent(slideOutPanel.transform);
        slideOutText.transform.localPosition = new Vector3(0, 0.072f, -0.004f);
        slideOutText.transform.localRotation = Quaternion.identity;
        slideOutText.transform.localScale = Vector3.one * 0.18f;
        
        var slideOutTextComp = slideOutText.GetComponent<TextMeshPro>();
        slideOutTextComp.text = "Player Selector";
        slideOutTextComp.color = new Color(0.1137f, 0.1059f, 0.0392f);
        slideOutTextComp.enableWordWrapping = false;
        slideOutTextComp.fontSizeMax = 1f;
        slideOutTextComp.fontSizeMin = 1f;
        slideOutTextComp.ForceMeshUpdate();
        
        ReplaySettings.playerTags.Clear();
        
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            
            var playerTag = GameObject.Instantiate(
                GameObjects.Gym.INTERACTABLES.Telephone20REDUXspecialedition.FriendScreen.PlayerTags.PlayerTag20.GetGameObject(),
                slideOutPanel.transform
            );
        
            playerTag.name = $"Player Tag {index}";
            playerTag.transform.localScale = Vector3.one * 0.5f;
            playerTag.transform.localPosition = new Vector3(0, -0.0073f + (-0.1091f * index), -0.0098f);
            playerTag.transform.localRotation = Quaternion.identity;

            playerTag.transform.GetChild(0).GetChild(1).GetChild(6).gameObject.SetActive(false);
        
            var button = playerTag.transform.GetChild(0).GetComponent<InteractionButton>();
            button.onPressed.RemoveAllListeners();
            button.onPressed.AddListener((UnityAction)(() => { ReplaySettings.selectedPlayer = ReplaySettings.PlayerAtIndex(index).player; }));
        
            ReplaySettings.playerTags.Add(button.transform.parent.GetComponent<PlayerTag>());
        }
        
        var nextPageButton = GameObject.Instantiate(playButton, slideOutPanel.transform);
        nextPageButton.name = "Next Page";
        nextPageButton.transform.localPosition = new Vector3(0.0822f, -0.4184f, 0.0371f);
        nextPageButton.transform.localRotation = Quaternion.Euler(270, 0, 0);
        nextPageButton.transform.localScale = Vector3.one * 0.8f;
        nextPageButton.transform.GetChild(0).GetChild(3).GetComponent<MeshRenderer>().material
            .SetTexture("_Texture", ReplayPlaybackControls.playSprite);
        
        var nextPageButtonComp = nextPageButton.transform.GetChild(0).GetComponent<InteractionButton>();
        nextPageButtonComp.enabled = true;
        nextPageButtonComp.onPressed.RemoveAllListeners();
        nextPageButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            ReplaySettings.SelectPlayerPage(ReplaySettings.currentPlayerPage + 1);
        }));
        
        var previousPageButton = GameObject.Instantiate(playButton, slideOutPanel.transform);
        previousPageButton.name = "Previous Page";
        previousPageButton.transform.localPosition = new Vector3(-0.0822f, -0.4184f, 0.0371f);
        previousPageButton.transform.localRotation = Quaternion.Euler(90, 180, 0);
        previousPageButton.transform.localScale = Vector3.one * 0.8f;
        previousPageButton.transform.GetChild(0).GetChild(3).GetComponent<MeshRenderer>().material
            .SetTexture("_Texture", ReplayPlaybackControls.playSprite);
        
        var previousPageButtonComp = previousPageButton.transform.GetChild(0).GetComponent<InteractionButton>();
        previousPageButtonComp.enabled = true;
        previousPageButtonComp.onPressed.RemoveAllListeners();
        previousPageButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            ReplaySettings.SelectPlayerPage(ReplaySettings.currentPlayerPage - 1);
        }));
        
        var pageNumberText = Create.NewText();
        
        pageNumberText.transform.SetParent(slideOutPanel.transform);
        pageNumberText.transform.localPosition = new Vector3(0f, -0.4152f, -0.0062f);
        pageNumberText.transform.localRotation = Quaternion.identity;
        pageNumberText.transform.localScale = Vector3.one * 0.4f;
        pageNumberText.name = "Page Number";
        
        var pageNumberTextComp = pageNumberText.GetComponent<TextMeshPro>();
        pageNumberTextComp.text = "0 / 0";
        pageNumberTextComp.color = Color.white;
        pageNumberTextComp.horizontalAlignment = HorizontalAlignmentOptions.Center;
        pageNumberTextComp.enableWordWrapping = false;
        pageNumberTextComp.ForceMeshUpdate();

        var pageSelectorGO = new GameObject("Page Selector Buttons");
        pageSelectorGO.transform.SetParent(slideOutPanel.transform, false);

        pageNumberText.transform.SetParent(pageSelectorGO.transform, true);
        nextPageButton.transform.SetParent(pageSelectorGO.transform, true);
        previousPageButton.transform.SetParent(pageSelectorGO.transform, true);
        
        var timelineRS = GameObject.Instantiate(timeline, replaySettingsGO.transform);
        timelineRS.name = "Timeline";
        timelineRS.layer = LayerMask.NameToLayer("Default");
        timelineRS.transform.localPosition = new Vector3(-0.0138f, -0.0039f, -0.0451f);
        timelineRS.transform.localScale = new Vector3(0.5168f, 0.0318f, 1.9291f);
        timelineRS.transform.localRotation = Quaternion.identity;
        
        var durationRS = GameObject.Instantiate(totalDuration, replaySettingsGO.transform);
        durationRS.name = "TotalDuration";
        durationRS.gameObject.layer = LayerMask.NameToLayer("Default");
        durationRS.transform.localPosition = new Vector3(0f, 0.0433f, -0.0444f);
        durationRS.transform.localScale = Vector3.one * 0.5f;
        durationRS.GetComponent<TextMeshPro>().enableWordWrapping = false;
        durationRS.GetComponent<TextMeshPro>().ForceMeshUpdate();
        
        var replayNameTitle = GameObject.Instantiate(playbackTitle.gameObject, replaySettingsGO.transform);
        replayNameTitle.transform.localPosition = new Vector3(0, 0.4527f, -0.0425f);
        replayNameTitle.transform.localScale = Vector3.one * 0.5f;
        replayNameTitle.name = "Replay Title";
        replayNameTitle.layer = LayerMask.NameToLayer("Default");
        
        var replayNameTitleComp = replayNameTitle.GetComponent<TextMeshPro>();
        replayNameTitleComp.enableWordWrapping = true;
        replayNameTitleComp.enableAutoSizing = true;
        replayNameTitleComp.fontSizeMax = 2f;
        replayNameTitleComp.fontSizeMin = 0.6f;
        replayNameTitleComp.GetComponent<RectTransform>().sizeDelta = new Vector2(1.2f, 0.1577f);
        replayNameTitleComp.alignment = TextAlignmentOptions.Top;
        
        var dateText = GameObject.Instantiate(playbackTitle.gameObject, replaySettingsGO.transform);
        dateText.transform.localPosition = new Vector3(0, 0.3854f, -0.0436f);
        dateText.transform.localScale = Vector3.one * 0.25f;
        dateText.name = "Date";
        dateText.layer = LayerMask.NameToLayer("Default");
        dateText.GetComponent<TextMeshPro>().enableWordWrapping = false;
        
        var deleteButton = GameObject.Instantiate(crystalizeButton, replaySettingsGO.transform);
        deleteButton.name = "DeleteReplay";
        deleteButton.transform.localPosition = new Vector3(0.1071f, -0.1486f, -0.0545f);
        deleteButton.transform.localRotation = Quaternion.identity;
        deleteButton.transform.localScale = Vector3.one * 1.1f;
        
        var deleteButtonComp = deleteButton.transform.GetChild(0).GetComponent<InteractionButton>();
        
        deleteButtonComp.onPressedAudioCall = loadReplayButtonComp.onPressedAudioCall;
        deleteButtonComp.longPressTime = 1f;
        
        deleteButtonComp.OnPressed.RemoveAllListeners();
        deleteButtonComp.OnPressed.AddListener((UnityAction)(() =>
        {
            if (ReplayFiles.explorer.currentIndex != -1)
            {
                if (crystalBreakCoroutine == null)
                {
                    ReplayCrystals.Crystal crystal = ReplayCrystals.Crystals.FirstOrDefault(c => c.ReplayPath == ReplayFiles.explorer.CurrentReplayPath);
                    crystalBreakCoroutine = MelonCoroutines.Start(ReplayCrystals.CrystalBreakAnimation(ReplayFiles.explorer.CurrentReplayPath, crystal));
                }
            }
            else 
            {
                ReplayError();
            }
        }));
        
        var srD = deleteButton.transform.GetChild(0).GetChild(3).GetComponent<SpriteRenderer>();
        var textureD = bundle.LoadAsset<Texture2D>("trashcan");
        srD.sprite = Sprite.Create(
            textureD,
            new Rect(0, 0, textureD.width, textureD.height),
            new Vector3(0.5f, 0.5f),
            100f
        );
        srD.color = new Color(1, 0.163f, 0.2132f, 1);
        srD.transform.localRotation = Quaternion.Euler(270, 180, 0);
        srD.transform.localScale = Vector3.one * 0.01f;
        srD.name = "DeleteIcon";
        
        var replayNameComp = replayNameTitle.GetComponent<TextMeshPro>();
        replayNameComp.enableAutoSizing = true;
        replayNameComp.fontSizeMin = 0.7f;
        replayNameComp.fontSizeMax = 1.2f;
        
        var dateComp = dateText.GetComponent<TextMeshPro>();
        dateComp.enableAutoSizing = true;
        dateComp.fontSizeMin = 0.7f;
        dateComp.fontSizeMax = 1.2f;
        
        var renameInstructions = GameObject.Instantiate(dateText, replaySettingsGO.transform);
        renameInstructions.name = "Rename Instructions";
        renameInstructions.transform.localPosition = new Vector3(0, 0.2898f, -0.0422f);
        renameInstructions.transform.localScale = Vector3.one * 0.55f;
        renameInstructions.SetActive(false);
        
        var renameInstructionsComp = renameInstructions.GetComponent<TextMeshPro>();
        renameInstructionsComp.text = "Please type on your keyboard to rename\n<#32a832>Enter - Confirm     <#cc1b1b>Esc - Cancel";
        renameInstructionsComp.enableWordWrapping = false;
        
        var deleteReplayText = GameObject.Instantiate(renameInstructions, deleteButton.transform);
        deleteReplayText.name = "DeleteText";
        deleteReplayText.transform.localPosition = new Vector3(0.0143f, 0.0713f, 0);
        deleteReplayText.SetActive(true);
        deleteReplayText.transform.localScale = Vector3.one * 0.2f;
        
        var deleteReplayTextComp = deleteReplayText.GetComponent<TextMeshPro>();
        deleteReplayTextComp.text = "<#cc1b1b>DELETE REPLAY";
        deleteReplayTextComp.enableWordWrapping = false;
        deleteReplayTextComp.ForceMeshUpdate();
        
        var copyPathButton = GameObject.Instantiate(deleteButton, replaySettingsGO.transform);
        copyPathButton.name = "CopyPathButton";
        copyPathButton.transform.localPosition = new Vector3(-0.0224f, -0.1486f, -0.0545f);
        copyPathButton.transform.localRotation = Quaternion.identity;
        
        copyPathButton.transform.GetChild(2).GetComponent<TextMeshPro>().text = "Copy Path";
        copyPathButton.transform.GetChild(2).GetComponent<TextMeshPro>().ForceMeshUpdate();
        copyPathButton.transform.GetChild(2).localPosition = new Vector3(0.0143f, 0.0713f, 0);
        
        var srCo = copyPathButton.transform.GetChild(0).GetChild(3).GetComponent<SpriteRenderer>();
        var textureCo = bundle.LoadAsset<Texture2D>("copytoclipboard");
        srCo.sprite = Sprite.Create(
            textureCo,
            new Rect(0, 0, textureCo.width, textureCo.height),
            new Vector3(0.5f, 0.5f),
            100f
        );
        srCo.color = Color.white;
        srCo.transform.localRotation = Quaternion.Euler(270, 180, 0);
        srCo.transform.localScale = Vector3.one * -0.015f;
        srCo.name = "CopyPathIcon";
        
        var copyPathButtonComp = copyPathButton.transform.GetChild(0).GetComponent<InteractionButton>();
        copyPathButtonComp.useLongPress = false;
        
        copyPathButtonComp.onPressed.RemoveAllListeners();
        copyPathButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            GUIUtility.systemCopyBuffer = ReplayFiles.explorer.CurrentReplayPath;
        }));
        
        replaySettings = replaySettingsPanel.AddComponent<ReplaySettings>();
        
        var renameButton = GameObject.Instantiate(deleteButton, replaySettingsGO.transform);
        renameButton.name = "RenameButton";
        renameButton.transform.localPosition = new Vector3(-0.1522f, -0.1486f, -0.0545f);
        renameButton.transform.localRotation = Quaternion.identity;
        
        var renameButtonComp = renameButton.transform.GetChild(0).GetComponent<InteractionButton>();
        renameButtonComp.onPressed.RemoveAllListeners();
        
        renameButtonComp.isToggleButton = true;
        renameButtonComp.useLongPress = false;
        renameButtonComp.onToggleFalseAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Unpress"];
        renameButtonComp.onToggleTrueAudioCall = ReplayCache.SFX["Call_GearMarket_GenericButton_Press"];
        renameButtonComp.onToggleStateChanged.AddListener((UnityAction<bool>)((bool toggleState) =>
        {
            if (ReplayFiles.explorer.currentIndex != -1)
            {
                replaySettings.OnRenamePressed(!toggleState);
            }
        }));
        
        renameButton.transform.GetChild(2).GetComponent<TextMeshPro>().text = "Rename";
        renameButton.transform.GetChild(2).GetComponent<TextMeshPro>().ForceMeshUpdate();
        renameButton.transform.GetChild(2).localPosition = new Vector3(0.0171f, 0.0713f, 0);
        
        var srRename = renameButton.transform.GetChild(0).GetChild(3).GetComponent<SpriteRenderer>();
        var textureRename = bundle.LoadAsset<Texture2D>("RenameIcon");
        srRename.sprite = Sprite.Create(
            textureRename,
            new Rect(0, 0, textureRename.width, textureRename.height),
            new Vector3(0.5f, 0.5f),
            100f
        );
        srRename.color = Color.white;
        srRename.transform.localRotation = Quaternion.Euler(270, 180, 0);
        srRename.transform.localScale = Vector3.one * 0.01f;
        srRename.name = "RenameIcon";

        var favoriteButton = GameObject.Instantiate(
            GameObjects.Gym.INTERACTABLES.Telephone20REDUXspecialedition.SettingsScreen.PreReportSection.ReportPlayerButton.GetGameObject(),
            replaySettingsGO.transform
        );

        favoriteButton.name = "Favorite Button";

        favoriteButton.transform.localPosition = new Vector3(-0.2489f, -0.0529f, -0.028f);
        favoriteButton.transform.localRotation = Quaternion.Euler(0, 270, 90);
        favoriteButton.transform.localScale = Vector3.one * 0.35f;
        favoriteButton.transform.GetChild(2).gameObject.SetActive(false);

        var favoriteButtonComp = favoriteButton.transform.GetChild(0).GetComponent<InteractionButton>();
        favoriteButtonComp.enabled = true;

        var trackedIcon = GameObjects.Gym.INTERACTABLES.Gearmarket.Itemhighlightwindow.TrackedIcon.GetGameObject();
        var favIcon = trackedIcon.GetComponent<SpriteRenderer>().sprite.texture;
        var favRenderer = favoriteButtonComp.transform.GetChild(5).GetComponent<MeshRenderer>();
        favRenderer.material.SetTexture("_Texture", favIcon);
        favRenderer.transform.localScale = Vector3.one * 0.0708f;

        var replayFavIcon = GameObject.Instantiate(trackedIcon, replaySettingsGO.transform);
        replayFavIcon.name = "Favorited Icon";
        replayFavIcon.transform.localPosition = new Vector3(-0.3248f, 0.5046f, -0.047f);
        replayFavIcon.transform.localRotation = Quaternion.Euler(0, 0, 22.0727f);
        replayFavIcon.transform.localScale = Vector3.one * 0.003f;
        replayFavIcon.SetActive(false);

        favoriteButtonComp.useLongPress = false;
        favoriteButtonComp.onPressed.RemoveAllListeners();
        favoriteButtonComp.onPressed.AddListener((UnityAction)(() =>
        {
            var currentEntry = ReplayFiles.explorer.currentReplayEntries[ReplayFiles.explorer.currentIndex];
            bool isFavorited = currentEntry.header.isFavorited;

            currentEntry.header.isFavorited = !isFavorited;
            ReplayArchive.WriteManifest(currentEntry.FullPath, currentEntry.header);

            ReplaySettings.favoritedIcon.SetActive(!isFavorited);

            ReplayFiles.ReloadReplays();
        }));

        ReplaySettings.replaySettingsGO = replaySettingsGO;
        ReplaySettings.deleteButton = deleteButtonComp;
        ReplaySettings.replayName = replayNameComp;
        ReplaySettings.dateText = dateComp;
        ReplaySettings.renameInstructions = renameInstructionsComp;
        ReplaySettings.renameButton = renameButtonComp;
        ReplaySettings.timeline = timelineRS;
        ReplaySettings.durationComp = durationRS;
        ReplaySettings.slideOutPanel = slideOutPanel;
        ReplaySettings.pageNumberText = pageNumberTextComp;
        ReplaySettings.povButton = povCameraButton;
        ReplaySettings.hideLocalPlayerToggle = hideLocalPlayerButton;
        ReplaySettings.openControlsButton = openControlsButton;
        ReplaySettings.favoritedIcon = replayFavIcon;
        
        // Replay Explorer

        var replayExplorerGO = new GameObject("Replay Explorer");
        replayExplorerGO.transform.SetParent(replaySettingsPanel.transform.GetChild(0), false);

        ReplaySettings.replayExplorerGO = replayExplorerGO;

        ReplayFiles.folderIcon = bundle.LoadAsset<Texture2D>("FolderIcon");
        ReplayFiles.folderIcon.hideFlags = HideFlags.DontUnloadUnusedAsset;
        ReplayFiles.replayIcon = bundle.LoadAsset<Texture2D>("ReplayIcon");
        ReplayFiles.replayIcon.hideFlags = HideFlags.DontUnloadUnusedAsset;
        
        for (int i = 0; i <= 5; i++)
        {
            var replayButton = GameObject.Instantiate(
                GameObjects.Gym.INTERACTABLES.Telephone20REDUXspecialedition.FriendScreen.PlayerTags.PlayerTag20.GetGameObject(),
                replayExplorerGO.transform
            );

            replayButton.name = $"Selection_{i}";
            replayButton.transform.localPosition = new Vector3(-0.0148f, 0.293f - (0.0935f * i), -0.0418f);

            var meshes = replayButton.transform.GetChild(0).GetChild(0);
            var TextandIcons = replayButton.transform.GetChild(0).GetChild(1);

            for (int j = 0; j < meshes.childCount; j++)
            {
                if (j != 0 && j != 1 && j != 3)
                    meshes.GetChild(j).gameObject.SetActive(false);
            }

            for (int j = 0; j < TextandIcons.childCount; j++)
            {
                if (j != 0 && j != 3)
                    TextandIcons.GetChild(j).gameObject.SetActive(false);
            }

            meshes.GetChild(0).localPosition = new Vector3(-0.2231f, 0.0344f, -0.0091f);
            
            meshes.GetChild(1).localPosition = new Vector3(0, 0.0356f, -0.0039f);
            meshes.GetChild(1).localScale = new Vector3(0.0324f, 0.2663f, 0.1858f);

            meshes.GetChild(3).localPosition = new Vector3(0.0318f, 0.0341f, -0.0095f);
            meshes.GetChild(3).localScale = new Vector3(0.0324f, 0.2165f, 0.1285f);
            
            GameObject.Destroy(meshes.GetChild(7).gameObject);

            TextandIcons.GetChild(0).localPosition = new Vector3(-0.1715f, 0.0113f, -0.0292f);
            var tmp = TextandIcons.GetChild(0).GetComponent<TextMeshPro>();
            var rectTransform = TextandIcons.GetChild(0).GetComponent<RectTransform>();
            tmp.fontSizeMin = 0.2f;
            tmp.alignment = TextAlignmentOptions.Left;
            rectTransform.sizeDelta = new Vector2(0.42f, 0.0502f);

            TextandIcons.GetChild(3).localPosition = new Vector3(-0.2241f, 0.035f, -0.0288f);
            TextandIcons.GetChild(3).localScale = Vector3.one * 0.0422f;
            
            replayButton.GetComponent<PlayerTag>().RemovePressedCallback();

            var haptics = ScriptableObject.CreateInstance<InteractionHapticsSignal>();
            haptics.duration = 0.05f;
            haptics.intensity = 0.5f;
            replayButton.transform.GetChild(0).GetComponent<InteractionButton>().onPressedHaptics = haptics;

            var renderer = replayButton.transform.GetChild(0).GetChild(0).GetChild(1).GetComponent<MeshRenderer>();
            var collider = replayButton.transform.GetChild(0).GetComponent<BoxCollider>();

            collider.center = replayButton.transform.GetChild(0).transform.InverseTransformPoint(renderer.bounds.center);
            collider.size = renderer.bounds.size;

            var favoriteIcon = GameObject.Instantiate(
                GameObjects.Gym.INTERACTABLES.Gearmarket.Itemhighlightwindow.TrackedIcon.GetGameObject(),
                TextandIcons
            );

            favoriteIcon.transform.localPosition = new Vector3(0.2455f, 0.0626f, -0.0338f);
            favoriteIcon.transform.localScale = Vector3.one * 0.002f;
            favoriteIcon.SetActive(false);
        }

        var PathGO = new GameObject("Path");
        PathGO.transform.SetParent(replayExplorerGO.transform, false);

        var pathBackgroundBlock = GameObject.Instantiate(GameObjects.Gym.INTERACTABLES.Telephone20REDUXspecialedition.FriendScreen.PlayerTags.PlayerTag20.GetGameObject()
            .transform.GetChild(0).GetChild(0).GetChild(1), PathGO.transform);

        pathBackgroundBlock.transform.localPosition = new Vector3(-0.0149f, 0.4088f, -0.0438f);
        pathBackgroundBlock.transform.localRotation = Quaternion.Euler(270, 90, 0);
        pathBackgroundBlock.transform.localScale = new Vector3(0.0324f, 0.2663f, 0.1098f);
        pathBackgroundBlock.name = "Path Background Block";

        var pathText = Create.NewText(":3", 1f, Color.white, Vector3.zero, Quaternion.identity);
        pathText.name = "Path Text";
        pathText.transform.SetParent(PathGO.transform);
        
        pathText.transform.localRotation = Quaternion.identity;
        pathText.transform.localPosition = new Vector3(-0.0112f, 0.4076f, -0.0645f);
        pathText.transform.localScale = Vector3.one * 0.95f;

        var pathTMP = pathText.GetComponent<TextMeshPro>();
        pathTMP.fontSizeMax = 0.3f;
        pathTMP.fontSizeMin = 0.1f;
        pathTMP.enableAutoSizing = true;
        pathTMP.enableWordWrapping = false;
        pathTMP.alignment = TextAlignmentOptions.Left;
        pathTMP.text = "Replays";
        pathTMP.color = new Color(0.102f, 0.051f, 0.0275f);

        var replayPageSelectorGO = GameObject.Instantiate(pageSelectorGO, replayExplorerGO.transform);
        replayPageSelectorGO.name = "Page Selector";
        
        replayPageSelectorGO.transform.localPosition = new Vector3(-0.0138f, 0.297f, -0.0365f);
        replayPageSelectorGO.transform.localScale = Vector3.one * 1.3f;

        var nextReplayPageBtn = replayPageSelectorGO.transform.GetChild(1).GetChild(0).GetComponent<InteractionButton>();
        var previousReplayPageBtn = replayPageSelectorGO.transform.GetChild(2).GetChild(0).GetComponent<InteractionButton>();
        
        nextReplayPageBtn.onPressed.RemoveAllListeners();
        nextReplayPageBtn.onPressed.AddListener((UnityAction)(() =>
        {
            ReplayFiles.explorer.currentPage = Clamp(++ReplayFiles.explorer.currentPage, 0, ReplayFiles.explorer.pageCount - 1);
            ReplayFiles.RefreshUI();
        }));
        
        previousReplayPageBtn.onPressed.RemoveAllListeners();
        previousReplayPageBtn.onPressed.AddListener((UnityAction)(() =>
        {
            ReplayFiles.explorer.currentPage = Clamp(--ReplayFiles.explorer.currentPage, 0, ReplayFiles.explorer.pageCount - 1);
            ReplayFiles.RefreshUI();
        }));

        foreach (var renderer in ReplayTable.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = true;

        foreach (var renderer in playbackControls.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = true;
        
        GameObject.DontDestroyOnLoad(ReplayTable);
        GameObject.DontDestroyOnLoad(crystalPrefab);
        GameObject.DontDestroyOnLoad(playbackControls);
        GameObject.DontDestroyOnLoad(clapperboardVFX);
        GameObject.DontDestroyOnLoad(markerPrefab);
        bundle.Unload(false);
    }
    
    public void PlayClapperboardVFX(Vector3 position, Quaternion rotation)
    {
        var clapperboard = GameObject.Instantiate(clapperboardVFX);
        clapperboard.SetActive(true);
        clapperboard.transform.localScale = Vector3.zero;
        clapperboard.transform.position = position;
        clapperboard.transform.rotation = rotation;

        var vfx = PoolManager.instance.GetPool("RockCamSpawn_VFX").FetchFromPool(position, rotation);
        vfx.transform.localPosition += new Vector3(0f, 0.1f, 0f);
        vfx.transform.localScale = Vector3.one * 0.9f;

        AudioManager.instance.Play(!Recording.isRecording ? ReplayCache.SFX["Call_RockCam_StartRecording"] : ReplayCache.SFX["Call_RockCam_StopRecording"], position);

        MelonCoroutines.Start(Utilities.LerpValue(
            () => clapperboard.transform.localScale,
            v => clapperboard.transform.localScale = v,
            Vector3.Lerp,
            Vector3.one * 5f,
            0.5f,
            Utilities.EaseInOut,
            () =>
            {
                MelonCoroutines.Start(Utilities.LerpValue(
                    () => clapperboard.transform.localScale,
                    v => clapperboard.transform.localScale = v,
                    Vector3.Lerp,
                    Vector3.zero,
                    0.5f,
                    Utilities.EaseInOut,
                    () =>
                    {
                        GameObject.Destroy(clapperboard);
                        AudioManager.instance.Play(ReplayCache.SFX["Call_RockCam_Despawn"], position);
                    }
                ));
            }
        ));
        
        MelonCoroutines.Start(Utilities.LerpValue(
            () => clapperboard.transform.localRotation,
            v => clapperboard.transform.localRotation = v,
            Quaternion.Slerp,
            rotation * Quaternion.Euler(0f, 17f, 0f),
            0.8f,
            Utilities.EaseInOut
        ));

        MelonCoroutines.Start(Utilities.LerpValue(
            () => clapperboard.transform.GetChild(0).localRotation,
            v => clapperboard.transform.GetChild(0).localRotation = v,
            Quaternion.Slerp,
            Quaternion.Euler(0f, 9.221f, 0f),
            0.5f,
            Utilities.EaseInOut,
            () =>
            {
                MelonCoroutines.Start(Utilities.LerpValue(
                    () => clapperboard.transform.GetChild(0).localRotation,
                    v => clapperboard.transform.GetChild(0).localRotation = v,
                    Quaternion.Slerp,
                    Quaternion.Euler(0f, 347.9986f, 0f),
                    0.5f,
                    Utilities.EaseInOut
                ));
            }
        ));
    }
    
    // ----- Replay Loading -----

    public void LoadSelectedReplay()
    {
        DebugLog(
            $"LoadReplay | Current:{currentScene} | Target:{ReplayFiles.currentHeader?.Scene} | Path:{ReplayFiles.explorer.CurrentReplayPath}"
        );
        
        if (ReplayFiles.explorer.currentIndex == -1)
        {
            ReplayError("Could not find file.");
            return;
        }
        
        if (currentScene == "Park" && (PhotonNetwork.CurrentRoom?.PlayerCount ?? 0) > 1)
        {
            ReplayError("Cannot start replay in room with more than 1 player.");
            return;
        }
        
        if (Playback.isPlaying)
            Playback.StopReplay();

        string targetScene = ReplayFiles.currentHeader.Scene;
        bool switchingScene = targetScene != currentScene;

        bool isCustomMap = targetScene is "Map0" or "Map1" && !string.IsNullOrWhiteSpace(ReplayFiles.currentHeader.CustomMap);
        bool isRawMapData = !string.IsNullOrWhiteSpace(ReplayFiles.currentHeader.CustomMap) && ReplayFiles.currentHeader.CustomMap.Split('|').Length > 15;
        
        DebugLog(
            $"SwitchingScene:{switchingScene} | Custom:{isCustomMap} | RawMap:{isRawMapData}"
        );

        GameObject replayCustomMap = null;

        if (ReplayFiles.currentHeader.CustomMap == "FlatLandSingle")
        {
            if (flatLandRoot?.gameObject.activeSelf == false)
            {
                switchingScene = false;
            }
            else
            {
                var button = flatLandRoot?.transform?.GetChild(1)?.GetChild(0)?.GetComponent<InteractionButton>();
                if (button == null)
                {
                    ReplayError("Could not load FlatLand Replay. Please make sure FlatLand is installed.");
                    return;
                }
                
                button.RPC_OnPressed();
                MelonCoroutines.Start(DelayedFlatLandLoad());
                return;
            }
        }
        
        if (switchingScene && isCustomMap && !isRawMapData)
        {
            replayCustomMap = Utilities.GetCustomMap(ReplayFiles.currentHeader.CustomMap);

            if (replayCustomMap == null)
                return;
        }

        if (switchingScene)
        {
            ReplayFiles.HideMetadata();

            int sceneIndex = targetScene switch
            {
                "Map0" => 3,
                "Map1" => 4,
                "Park" => 2,
                "Gym" => 1,
                _ => -1
            };

            if (sceneIndex == -1)
            {
                ReplayError($"Unknown scene '{targetScene}'");
                return;
            }

            if (sceneIndex == 2)
            {
                var parkboard = GameObjects.Gym.INTERACTABLES.Parkboard.
                    GetGameObject().
                    GetComponent<ParkBoardGymVariant>();

                parkboard.doorPolicySlider.SetStep(1);
                parkboard.HostPark();

                ReplayPlayback.isReplayScene = true;
            }
            else
            {
                MelonCoroutines.Start(
                    Utilities.LoadMap(sceneIndex, 2.5f, () =>
                    {
                        Playback.LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
                        ReplayFiles.ShowMetadata();

                        switch (targetScene)
                        {
                            case "Map0":
                            {
                                GameObjects.Map0.Logic.MatchHandler.GetGameObject().SetActive(false);
                                break;
                            }
                            case "Map1":
                            {
                                GameObjects.Map1.Logic.MatchHandler.GetGameObject().SetActive(false);
                                GameObjects.Map1.Logic.SceneProcessors.GetGameObject().SetActive(false);
                                break;
                            }
                            case "Park":
                            {
                                GameObjects.Park.LOGIC.ParkInstance
                                    .GetGameObject()
                                    .SetActive(false);
                                break;
                            }
                        }

                        if (isCustomMap)
                        {
                            if (replayCustomMap != null)
                            {
                                replayCustomMap.SetActive(true);
                            }
                            else if (isRawMapData)
                            {
                                var type = RegisteredMelons.FirstOrDefault(m => m.Info.Name == "CustomMultiplayerMaps")?.MelonAssembly?.Assembly?.GetTypes().FirstOrDefault(t => t.Name == "main");
                                if (type != null)
                                {
                                    var method = type.GetMethod(
                                        "LoadCustomMap", 
                                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                                        null,
                                        new[] { typeof(string[]) },
                                        null
                                    );
                                    
                                    string[] split = ReplayFiles.currentHeader.CustomMap.Split('|');
                                    method?.Invoke(null, new object[] { split });
                                }
                            }
                            
                            if (targetScene == "Map0")
                                GameObjects.Map0.Scene
                                    .GetGameObject()
                                    .SetActive(false);
                            else if (targetScene == "Map1")
                                GameObjects.Map1.Scene
                                    .GetGameObject()
                                    .SetActive(false);
                        }

                        SimpleScreenFadeInstance.Progress = 0f;
                    }, 2f)
                );
            }
        }
        else
        {
            if (currentScene == "Park")
                MelonCoroutines.Start(DelayedParkLoad());
            else
                Playback.LoadReplay(ReplayFiles.explorer.CurrentReplayPath);
            
            SimpleScreenFadeInstance.Progress = 0f;
        }
    }
    
    // ----- Update Loops -----
    
    public override void OnUpdate()
    {
        if (!UIInitialized) return;
        
        HandleReplayPose();
        ReplayPlaybackControls.Update();

        if (currentScene != "Loader")
            ReplayCrystals.HandleCrystals();

        // Why does the nameplate appear when a replay loads?
        // Who knows! I doubt this will conflict with other mods
        if (LocalPlayer?.Controller != null)
            LocalPlayer.Controller.PlayerNameTag.gameObject.SetActive(false);
        
        if (currentScene != "Gym" || replayTable == null || replayTable.gameObject || replayTable.metadataText == null)
            return;

        bool flatLandActive = flatLandRoot != null && flatLandRoot?.activeSelf == true;

        if (lastFlatLandActive == flatLandActive)
            return;

        lastFlatLandActive = flatLandActive;
        
        replayTable.gameObject.SetActive(true);
        replayTable.metadataText.gameObject.SetActive(true);

        if (!flatLandActive)
        {
            replayTable.tableFloat.startPos = new Vector3(5.9641f, 1.1323f, -4.5477f);
            replayTable.metadataTextFloat.startPos = new Vector3(5.9641f, 1.1323f, -4.5477f);
            
            replayTable.tableOffset = -0.4f;
            replayTable.transform.rotation = Quaternion.Euler(270, 253.3632f, 0);
        }
        else
        {
            replayTable.tableFloat.startPos = new Vector3(5.9506f, 1.3564f, 4.1906f);
            replayTable.metadataTextFloat.startPos = new Vector3(5.9575f, 1.8514f, 4.2102f);
        
            replayTable.tableOffset = 0f;
            replayTable.transform.localRotation = Quaternion.Euler(270, 121.5819f, 0);
        }
    }

    public override void OnLateUpdate()
    {
        if (!UIInitialized) return;
        
        Recording?.HandleRecording();
        Playback?.HandlePlayback();
    }

    public override void OnFixedUpdate()
    {
        if (currentScene == "Loader" || !UIInitialized) return;
        
        TryHandleController(
            LeftHandControls.Value,
            Calls.ControllerMap.LeftController.GetPrimary(),
            Calls.ControllerMap.LeftController.GetSecondary(),
            true
        );
        
        TryHandleController(
            RightHandControls.Value,
            Calls.ControllerMap.RightController.GetPrimary(),
            Calls.ControllerMap.RightController.GetSecondary(),
            false
        );
    }
    
    bool IsFramePose(Transform handA, Transform handB)
    {
        Vector3 headRight = head.right;

        Vector3 fingerA = handA.forward;
        Vector3 thumbA  = handA.up;
        Vector3 palmA   = handA.right;

        Vector3 fingerB = handB.forward;
        Vector3 thumbB  = handB.up;
        Vector3 palmB   = -handB.right;

        Vector3 toHeadA = (head.position - handA.position).normalized;
        Vector3 toHeadB = (head.position - handB.position).normalized;

        float dotSide = Vector3.Dot(fingerA, headRight);
        bool A_pointsSideways = Abs(dotSide) > 0.7f;

        float dotPalmHeadA = Vector3.Dot(palmA, toHeadA);
        bool A_palmToHead = dotPalmHeadA > 0.6f;

        float dotThumbUpA = Vector3.Dot(thumbA, Vector3.up);
        bool A_thumbUp = dotThumbUpA > 0.6f;

        float dotOpposite = Vector3.Dot(fingerA, fingerB);
        bool B_pointsOpposite = dotOpposite < -0.7f;

        float dotPalmHeadB = Vector3.Dot(palmB, toHeadB);
        bool B_palmAwayFromHead = dotPalmHeadB < -0.6f;

        float dotThumbDownB = Vector3.Dot(thumbB, Vector3.up);
        bool B_thumbDown = dotThumbDownB < -0.6f;

        float dist = Vector3.Distance(handA.position, handB.position);
        float maxDist =
            LocalPlayer.Data.PlayerMeasurement.ArmSpan
            * (0.30f / errorsArmspan);

        bool closeEnough = dist < maxDist;

        return
            A_pointsSideways &&
            A_palmToHead &&
            A_thumbUp &&
            B_pointsOpposite &&
            B_palmAwayFromHead &&
            B_thumbDown &&
            closeEnough;
    }

    bool IsPausePlayPose(Transform left, Transform right)
    {
        float fingerUpDot = Vector3.Dot(left.forward.normalized, head.up);
        bool leftHandFlat = Abs(fingerUpDot) < Cos(60f * Deg2Rad);

        float palmDownDot = Vector3.Dot(left.right.normalized, -head.up);
        bool leftPalmDown = palmDownDot > Cos(60f * Deg2Rad);

        bool leftHandCorrect = leftHandFlat && leftPalmDown;
        
        float fingerVerticalDot = Vector3.Dot(right.forward.normalized, head.up);
        bool rightHandVertical = Abs(fingerVerticalDot) >= Cos(35f * Deg2Rad);

        float palmLeftDot = Vector3.Dot((-right.right).normalized, -head.right);
        bool rightPalmFacingLeft = palmLeftDot > 0.5f;

        bool rightHandCorrect = rightHandVertical && rightPalmFacingLeft;

        float dist = Vector3.Distance(left.position, right.position);
        float maxDist = LocalPlayer.Data.PlayerMeasurement.ArmSpan * (0.125f / errorsArmspan);

        bool handsCloseEnough = dist < maxDist;
        bool leftAboveRight = left.position.y > right.position.y;

        return leftHandCorrect && leftAboveRight && rightHandCorrect && handsCloseEnough;
    }

    public void HandleReplayPose()
    {
        if (currentScene == "Loader")
            return;

        if (leftHand == null || rightHand == null || head == null)
        {
            var controller = LocalPlayer?.Controller;
            if (controller == null) return;

            var vr = controller.transform?.childCount >= 3 ? controller.transform.GetChild(2) : null;
            if (vr == null || vr.childCount < 3) return;

            var headParent = vr.GetChild(0);
            if (headParent.childCount < 1) return;

            leftHand = vr.GetChild(1);
            rightHand = vr.GetChild(2);
            head = headParent.GetChild(0);
        }

        bool pose = IsFramePose(leftHand, rightHand);

        bool triggersHeld =
            Calls.ControllerMap.LeftController.GetTrigger() > 0.8f &&
            Calls.ControllerMap.RightController.GetTrigger() > 0.8f;

        if (pose &&
            Calls.ControllerMap.LeftController.GetGrip() > 0.8f &&
            Calls.ControllerMap.RightController.GetGrip() > 0.8f)
        {
            heldTime += Time.deltaTime;
            soundTimer += Time.deltaTime;

            if (soundTimer >= 0.5f && !hasPlayed)
            {
                soundTimer -= 0.5f;
                AudioManager.instance.Play(
                    triggersHeld 
                        ? ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"]
                        : ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"],
                    head.position
                );
            }

            if (heldTime >= 2f && !hasPlayed)
            {
                hasPlayed = true;

                if (triggersHeld)
                {
                    if (Playback.isPlaying)
                    {
                        if (ReplayPlaybackControls.playbackControlsOpen && 
                            Vector3.Distance(ReplayPlaybackControls.playbackControls.transform.position, head.position) < LocalPlayer.Data.PlayerMeasurement.ArmSpan
                        )
                            ReplayPlaybackControls.Close();
                        else
                            ReplayPlaybackControls.Open();
                    }
                    else
                    {
                        ReplayError();
                    }
                }
                else
                {
                    PlayClapperboardVFX(
                        head.position + head.forward * 1.2f + new Vector3(0f, -0.1f, 0f),
                        Quaternion.Euler(270f, head.eulerAngles.y + 180f, 0f)
                    );
                    
                    if (Recording.isRecording)
                        Recording.StopRecording();
                    else
                        Recording.StartRecording();
                }
            }
        }
        else
        {
            hasPlayed = false;
            heldTime = 0f;
            soundTimer = 0f;
        }

        bool isPausePose = IsPausePlayPose(leftHand, rightHand);

        if (isPausePose &&
            Calls.ControllerMap.LeftController.GetTrigger() < 0.4f &&
            Calls.ControllerMap.RightController.GetTrigger() < 0.4f &&
            Calls.ControllerMap.LeftController.GetGrip() < 0.4f &&
            Calls.ControllerMap.RightController.GetGrip() < 0.4f
           )
        {
            if (!Playback.hasPaused)
            {
                Playback.hasPaused = true;
                Playback.TogglePlayback(Playback.isPaused);
            }
        }
        else if (!isPausePose)
        {
            Playback.hasPaused = false;
        }
    }

    public void TryHandleController(
        ControllerAction action,
        float primary,
        float secondary,
        bool isLeft
    )
    {
        void PlayHaptics()
        {
            var haptics = LocalPlayer.Controller.GetSubsystem<PlayerHaptics>();
            
            if (EnableHaptics.Value)
            {
                if (isLeft)
                    haptics.PlayControllerHaptics(1f, 0.05f, 0, 0);
                else
                    haptics.PlayControllerHaptics(0, 0, 1f, 0.05f);
            }
        }

        if (primary <= 0 || secondary <= 0)
            return;

        if (Time.time - lastTriggerTime <= 1f)
            return;
        
        lastTriggerTime = Time.time;

        if (action == ControllerAction.None)
            return;
        
        DebugLog($"Controller Action | {(isLeft ? "Left" : "Right")} | {action}");
        
        switch (action)
        {
            case ControllerAction.ToggleRecording:
            {
                if (Recording.isRecording)
                    Recording.StopRecording();
                else
                    Recording.StartRecording();

                break;
            }
            
            case ControllerAction.SaveReplayBuffer:
            {
                Recording.SaveReplayBuffer();
                PlayHaptics();
                break;
            }

            case ControllerAction.AddMarker:
            {
                if (!Recording.isRecording && !Recording.isBuffering)
                    break;

                Recording.AddMarker("core.manual", Color.white);
                PlayHaptics();
            
                AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], head.position);
                break;
            }
        
            default:
                ReplayError($"'{action}' is not a valid binding ({(isLeft ? "Left Controller" : "Right Controller")}).");
                break;
        }
    }
}

[RegisterTypeInIl2Cpp]
public class TableFloat : MonoBehaviour
{
    public float amplitude = 0.25f;
    public float speed = 1f;

    public float stopRadius = 2f;
    public float resumeSpeed = 6f;

    private float floatTime;
    private float timeScale = 1f;

    public Vector3 startPos;
    public float targetY;

    void Awake()
    {
        startPos = transform.localPosition;
    }

    void Update()
    {
        if (Main.instance.head == null)
            return;
        
        startPos.y = Lerp(
            startPos.y, 
            targetY + Main.instance.tableOffset.Value, 
            Time.deltaTime * 4f
        );
        
        float d = (Main.instance.head.position - transform.position).magnitude;
        bool shouldStop = d < stopRadius;

        timeScale = Lerp(
            timeScale,
            shouldStop ? 0f : 1f,
            Time.deltaTime * resumeSpeed
        );

        floatTime += Time.deltaTime * speed * timeScale;
        
        float y = Sin(floatTime) * amplitude;
        transform.localPosition = startPos + Vector3.up * y;
    }
}

[RegisterTypeInIl2Cpp]
public class ReplayTable : MonoBehaviour
{
    public GameObject TableRoot;
    public TableFloat tableFloat;
    public TableFloat metadataTextFloat;

    public InteractionButton nextButton;
    public InteractionButton previousButton;
    public InteractionButton loadButton;
    public InteractionButton crystalizeButton;

    public TextMeshPro replayNameText;
    public TextMeshPro indexText;
    public TextMeshPro metadataText;
    public TextMeshPro heisenhouserText;
    
    public float desiredTableHeight = 1.5481f;
    public float tableOffset = 0f;
    public float desiredMetadataTextHeight = 1.8513f;

    public bool isReadingCrystal = false;

    public void Start()
    {
        tableFloat = TableRoot.GetComponent<TableFloat>();
        metadataTextFloat = metadataText.GetComponent<TableFloat>();
    }

    public void Update()
    {
        if (Main.currentScene != "Loader" && Main.LocalPlayer != null)
        {
            float playerArmspan = Main.LocalPlayer.Data.PlayerMeasurement.ArmSpan;
        
            if (tableFloat != null)
                tableFloat.targetY = playerArmspan * (desiredTableHeight / Main.errorsArmspan) + tableOffset;
        
            if (metadataTextFloat != null)
                metadataTextFloat.targetY = playerArmspan * (desiredMetadataTextHeight / Main.errorsArmspan) + tableOffset;
            
            ReplayCrystals.Crystal target = ReplayCrystals.FindClosestCrystal(
                transform.position + new Vector3(0, 0.4f, 0),
                0.5f
            );

            if (target == null && !SceneManager.instance.IsLoadingScene)
            {
                if (ReplayFiles.metadataHidden && ReplayFiles.explorer.currentIndex != -1)
                    ReplayFiles.ShowMetadata();

                isReadingCrystal = false;
            }
            else
            {
                if (!ReplayFiles.metadataHidden)
                    ReplayFiles.HideMetadata();

                if (!isReadingCrystal && target != null && !target.isGrabbed && target.hasLeftTable && !target.isAnimation)
                {
                    isReadingCrystal = true;
                    target.isAnimation = true;
                    MelonCoroutines.Start(ReplayCrystals.ReadCrystal(target));
                }
            }
        }
    }
}
