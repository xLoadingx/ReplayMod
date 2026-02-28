using System;
using System.Collections;
using System.Linq;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Networking.MatchFlow;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Social.Phone;
using Il2CppTMPro;
using MelonLoader;
using ReplayMod.Replay;
using ReplayMod.Replay.Files;
using ReplayMod.Replay.UI;
using RumbleModdingAPI;
using RumbleModdingAPI.RMAPI;
using RumbleModUI;
using RumbleModUIPlus;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using BuildInfo = ReplayMod.Core.BuildInfo;
using InteractionButton = Il2CppRUMBLE.Interactions.InteractionBase.InteractionButton;
using Main = ReplayMod.Core.Main;
using Mod = RumbleModUIPlus.Mod;
using Random = UnityEngine.Random;
using Tags = RumbleModUIPlus.Tags;
using Utilities = ReplayMod.Replay.Utilities;

[assembly: MelonInfo(typeof(Main), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("RumbleModdingAPI","RumbleModUIPlus")]

namespace ReplayMod.Core;

public static class BuildInfo
{
    public const string Name = "ReplayMod";
    public const string Author = "ERROR";
    public const string Version = "1.0.0";
    public const string FormatVersion = "1.0.0";
}

public class Main : MelonMod
{
    // Runtime
    public static Main instance;
    public Main() => instance = this;
    public static string currentScene => Calls.Scene.GetSceneName();

    public static ReplayPlayback Playback;
    public static ReplayRecording Recording;
    
    // Local player
    public static Player LocalPlayer => PlayerManager.instance.localPlayer;
    public Transform leftHand, rightHand, head;
    
    // Recording FX / timers
    public GameObject clapperboardVFX;
    public bool hasPlayed;
    public float heldTime, soundTimer = 0f;
    public float lastTriggerTime = 0f;
    
    // UI
    public ReplayTable replayTable;
    public GameObject flatLandRoot;
    public bool? lastFlatLandActive;

    public ReplaySettings replaySettings;
    public object crystalBreakCoroutine;
    
    // ------ Settings ------
    
    public const float errorsArmspan = 1.2744f;
    
    public static Mod replayMod = new();

    // Recording
    public ModSetting<int> TargetRecordingFPS = new();
    public ModSetting<bool> AutoRecordMatches = new();
    public ModSetting<bool> AutoRecordParks = new();
    
    public ModSetting<bool> HandFingerRecording = new();
    public ModSetting<bool> CloseHandsOnPose = new();
    
    // Automatic Markers - Match End
    public ModSetting<bool> EnableMatchEndMarker = new();
    
    // Automatic Markers - Round End
    public ModSetting<bool> EnableRoundEndMarker = new();
    
    // Automatic Markers - Large Damage
    public ModSetting<bool> EnableLargeDamageMarker = new();
    public ModSetting<int> DamageThreshold = new();
    public ModSetting<float> DamageWindow = new();
    
    // Playback
    public ModSetting<bool> StopReplayWhenDone = new();
    public ModSetting<bool> PlaybackControlsFollow = new();
    public ModSetting<bool> DestroyControlsOnPunch = new();
    
    // Replay Buffer
    public ModSetting<bool> ReplayBufferEnabled = new();
    public ModSetting<int> ReplayBufferDuration = new();

    // Controls
    public ModSetting<string> LeftHandControls = new();
    public ModSetting<string> RightHandControls = new();

    public ModSetting<bool> EnableHaptics = new();
    
    // Other
    public ModSetting<float> tableOffset = new();
    public ModSetting<bool> enableDebug = new();
    public ModSettingFolder extensionsFolder;
    
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
        if ((bool)instance.enableDebug.SavedValue && !string.IsNullOrEmpty(message))
            instance.LoggerInstance.Warning(message);
    }
    
    // ----- Init -----
    
    public override void OnLateInitializeMelon()
    {
        UI.instance.UI_Initialized += OnUIInitialized;
        Actions.onMapInitialized += (scene) => OnMapInitialized();
        
        Actions.onRoundEnded += () =>
        {
            if (!(bool)EnableRoundEndMarker.SavedValue)
                return;

            Recording.AddMarker("core.roundEnded", new Color(0.7f, 0.6f, 0.85f));
        };

        Actions.onMatchEnded += () =>
        {
            if ((bool)EnableMatchEndMarker.SavedValue)
                Recording.AddMarker("core.matchEnded", Color.black);
            
            if (Recording.isRecording) Recording.StopRecording();
        };
        
        ReplayFiles.Init();

        Recording = new();
        Playback = new(Recording);

        DebugLog("ReplayMod initialized");
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
    }
    
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (currentScene == "Loader")
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
        var recordingIcon = Create.NewText().GetComponent<TextMeshPro>();
        recordingIcon.transform.SetParent(LocalPlayer.Controller.GetSubsystem<PlayerUI>().transform.GetChild(0));
        recordingIcon.name = "Replay Recording Icon";
        recordingIcon.color = new Color(0, 1, 0, 0);
        recordingIcon.text = "●";
        recordingIcon.ForceMeshUpdate();
        recordingIcon.transform.localPosition = new Vector3(0.2313f, 0.0233f, 0.9604f);
        recordingIcon.transform.localRotation = Quaternion.Euler(20.2549f, 18.8002f, 0);
        recordingIcon.transform.localScale = Vector3.one * 0.4f;
        
        ReplayRecording.recordingIcon = recordingIcon;
        
        if (((currentScene is "Map0" or "Map1" && (bool)AutoRecordMatches.SavedValue && PlayerManager.instance.AllPlayers.Count > 1) || (currentScene == "Park" && (bool)AutoRecordParks.SavedValue)) && !ReplayPlayback.isReplayScene)
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
            
            replayTable.tableOffset = 0f;
            replayTable.transform.localRotation = Quaternion.Euler(270, 121.5819f, 0);
        }
        else if (currentScene == "Park")
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);
            
            replayTable.tableFloat.startPos = new Vector3(-28.9436f, -1.5689f, -6.9218f);
            replayTable.metadataTextFloat.startPos = new Vector3(-28.9499f, -2.0639f, -6.9414f);
            replayTable.tableOffset = -3.06f;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 0, 0);

            if (ReplayPlayback.isReplayScene)
                MelonCoroutines.Start(DelayedParkLoad());

        } 
        else if (currentScene == "Map0" && PlayerManager.instance.AllPlayers.Count == 1)
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);

            replayTable.tableFloat.startPos = new Vector3(0.411f, -4.4577f, 18.312f);
            replayTable.metadataTextFloat.startPos = new Vector3(0.411f, -4.027f, 18.3094f);
            replayTable.tableOffset = -6;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 90f, 0f);
        } 
        else if (currentScene == "Map1" && PlayerManager.instance.AllPlayers.Count == 1)
        {
            replayTable.TableRoot.SetActive(true);
            replayTable.metadataText.gameObject.SetActive(true);

            replayTable.tableFloat.startPos = new Vector3(0.991f, 1.1872f, 9.8221f);
            replayTable.metadataTextFloat.startPos = new Vector3(0.991f, 1.1872f, 9.8221f);
            replayTable.tableOffset = -0.35f;
            replayTable.transform.localRotation = Quaternion.Euler(270f, 90f, 0f);
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
        
        ReplayPlaybackControls.Close();
        
        if (currentScene != "Loader" && (bool)ReplayBufferEnabled.SavedValue)
            Recording.StartBuffering();
        
        if (Recording.isRecording || Recording.isBuffering)
            Recording.SetupRecordingData();

        var vr = LocalPlayer.Controller.transform.GetChild(2);
        replayTable.metadataText.GetComponent<LookAtPlayer>().lockX = true;
        replayTable.metadataText.GetComponent<LookAtPlayer>().lockZ = true;

        leftHand = vr.GetChild(1);
        rightHand = vr.GetChild(2);
        head = vr.GetChild(0).GetChild(0);

        Playback.SetPlaybackSpeed(1f);
        ReplayPlayback.isReplayScene = false;
    }

    IEnumerator DelayedParkLoad()
    {
        if ((bool)EnableHaptics.SavedValue)
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

    public void OnUIInitialized()
    {
        replayMod.ModName = BuildInfo.Name;
        replayMod.ModVersion = BuildInfo.Version;
        replayMod.ModFormatVersion = BuildInfo.Version;

        replayMod.SetFolder("ReplayMod/Settings");
        replayMod.AddDescription("Description", "", "A mod that records scenes into a 3d file that you can replay", new Tags { IsSummary = true });

        var recordingFolder = replayMod.AddFolder("Recording", "Controls how and when gameplay is recorded into replays.");

        TargetRecordingFPS = replayMod.AddToList("Recording FPS", 50, "The target frame rate used when recording replays.\nThis is limited by the game's actual frame rate.", new Tags());
        AutoRecordMatches = replayMod.AddToList("Automatically Record Matches", true, 0, "Automatically start recording when you join a match", new Tags());
        AutoRecordParks = replayMod.AddToList("Automatically Record Parks", false, 0, "Automatically start recording when you join a park", new Tags());
        
        HandFingerRecording = replayMod.AddToList("Finger Animation Recording", true, 0, "Controls whether finger input values are recorded into the replay.", new Tags());
        CloseHandsOnPose = replayMod.AddToList("Close Hands On Pose", true, 0, "Closes the hands of a clone when they do a pose.", new Tags());
        
        var automaticMarkersFolder = replayMod.AddFolder("Automatic Markers", "Automatically adds markers to replays when notable events occur.");

        var matchEndFolder = replayMod.AddFolder("Match End", "Settings for markers added when a match ends.");
        EnableMatchEndMarker = replayMod.AddToList("Enable Match End Marker", true, 0, "Automatically adds a marker at the end of a match.", new Tags());
        matchEndFolder.AddSetting(EnableMatchEndMarker);

        var roundEndFolder = replayMod.AddFolder("Round End", "Settings for markers added at the end of each round.");
        EnableRoundEndMarker = replayMod.AddToList("Enable Round End Marker", true, 0, "Automatically adds a marker at the end of a round.", new Tags());
        roundEndFolder.AddSetting(EnableRoundEndMarker);

        var largeDamageFolder = replayMod.AddFolder("Large Damage", "Settings for markers triggered by bursts of high damage.");
        EnableLargeDamageMarker = replayMod.AddToList("Enable Large Damage Marker", false, 0, "Automatically adds a marker when a player takes a large amount of damage in a short amount of time.", new Tags());
        DamageThreshold = replayMod.AddToList("Damage Threshold", 12, "The minimum total damage required to create a marker.", new Tags());
        DamageWindow = replayMod.AddToList("Damage Window (seconds)", 1f, "The time window (in seconds) during which damage is summed to determine whether a marker should be created.", new Tags());
        
        largeDamageFolder.AddSetting(EnableLargeDamageMarker)
            .AddSetting(DamageThreshold)
            .AddSetting(DamageWindow);

        automaticMarkersFolder.AddSetting(matchEndFolder)
            .AddSetting(roundEndFolder)
            .AddSetting(largeDamageFolder);
        
        recordingFolder.AddSetting(TargetRecordingFPS)
            .AddSetting(AutoRecordMatches)
            .AddSetting(AutoRecordParks)
            .AddSetting(HandFingerRecording)
            .AddSetting(CloseHandsOnPose)
            .AddSetting(automaticMarkersFolder);

        var playbackFolder = replayMod.AddFolder("Playback", "Settings for playing back replays.");
        
        StopReplayWhenDone = replayMod.AddToList("Stop Replay On Finished", false, 0, "Stops a replay when it reaches the end or beginning of its duration.", new Tags());
        PlaybackControlsFollow = replayMod.AddToList("Playback Controls Follow Player", false, 0, "Makes the playback controls menu follow you when opened.", new Tags());
        DestroyControlsOnPunch = replayMod.AddToList("Destroy Controls On Punch", true, 0, "Destroys the playback controls when you punch the slab hard enough.", new Tags());
        
        playbackFolder.AddSetting(StopReplayWhenDone);
        playbackFolder.AddSetting(PlaybackControlsFollow);
        playbackFolder.AddSetting(DestroyControlsOnPunch);
        
        var replayBufferFolder = replayMod.AddFolder("Replay Buffer", "Settings for the replay buffer used to save recent gameplay.");

        ReplayBufferEnabled = replayMod.AddToList("Enable Replay Buffer", false, 0, "Keeps a rolling buffer of recent gameplay that can be saved as a replay.", new Tags());
        ReplayBufferDuration = replayMod.AddToList("Replay Buffer Duration (seconds)", 30, "How much gameplay time (in seconds) is kept in the replay buffer.", new Tags());
        
        replayBufferFolder.AddSetting(ReplayBufferEnabled);
        replayBufferFolder.AddSetting(ReplayBufferDuration);
        
        var controlsFolder = replayMod.AddFolder("Controls", "Controller bindings and feedback settings for replay actions.");
        
        LeftHandControls = replayMod.AddToList("Left Controller Binding", "None",
            "Selects the action performed when both buttons on the left controller are pressed at the same time.\n" +
            "Possible values:\n" +
            "- Toggle Recording\n" +
            "- Save Replay Buffer\n" +
            "- Add Marker (adds an event marker at the current time in a recording)\n" +
            "- None\n" +
            "Actions can be seperated by a comma to include multiple actions on press.", new Tags());
        
        RightHandControls = replayMod.AddToList("Right Controller Binding", "None",
            "Selects the action performed when both buttons on the right controller are pressed at the same time.\n" +
            "Possible values:\n" +
            "- Toggle Recording\n" +
            "- Save Replay Buffer\n" +
            "- Add Marker (adds an event marker at the current time in a recording)\n" +
            "- None\n" +
            "Actions can be seperated by a comma to include multiple actions on press.", new Tags());

        EnableHaptics = replayMod.AddToList("Enable Haptics", true, 0, "Plays controller haptics when actions such as saving a replay or adding a marker are performed.", new Tags());

        controlsFolder.AddSetting(LeftHandControls)
            .AddSetting(RightHandControls)
            .AddSetting(EnableHaptics);

        if (ReplayAPI.Extensions.Any())
        {
            extensionsFolder = replayMod.AddFolder("Extensions", "Settings for all registered extensions");

            foreach (var ext in ReplayAPI.Extensions)
            {
                var extensionFolder = replayMod.AddFolder(ext.Id, $"Settings for the extension '{ext.Id}'");
                extensionsFolder.AddSetting(extensionFolder);

                var extensionToggle = replayMod.AddToList($"Toggle {ext.Id}", true, 0, "Toggles the extension on/off", new Tags());
                extensionFolder.AddSetting(extensionToggle);

                ext.Settings = extensionFolder;
                ext.Enabled = extensionToggle;
            }
        }
        
        var otherFolder = replayMod.AddFolder("Other", "Miscellaneous settings.");

        tableOffset = replayMod.AddToList("Replay Table Height Offset", 0f, "Adjusts the vertical offset of the replay table in meters.\nUseful if the table feels too high or too low.", new Tags());
        enableDebug = replayMod.AddToList("Enable Debug", false, 0, "Enables debug logs.\nIf something breaks, turn this on and wait for it to happen again.\nInclude your log when reporting bugs to ERROR.", new Tags());
        
        otherFolder.AddSetting(tableOffset);
        otherFolder.AddSetting(enableDebug);

        ReplayBufferEnabled.SavedValueChanged += (obj, sender) =>
        {
            if ((bool)ReplayBufferEnabled.Value && !Recording.isBuffering)
                Recording.StartBuffering();
            
            Recording.isBuffering = (bool)ReplayBufferEnabled.Value;
        };
        
        var allowedBindings = new[] { "Toggle Recording", "Save Replay Buffer", "Add Marker", "None" };

        bool IsValidBindingList(string input)
        {
            return input
                .Split(',')
                .Select(s => s.Trim())
                .All(binding => 
                    allowedBindings.Contains(binding, StringComparer.OrdinalIgnoreCase)
                );
        }
        
        LeftHandControls.SavedValueChanged += (obj, sender) =>
        {
            string value = (string)LeftHandControls.Value;
            if (!IsValidBindingList(value)) 
                ReplayError($"'{value}' is not a valid binding (Left Controller)");
        };

        RightHandControls.SavedValueChanged += (obj, sender) =>
        {
            string value = (string)RightHandControls.Value;
            if (!IsValidBindingList(value))
                ReplayError($"'{value}' is not a valid binding (Right Controller).");
        };
        
        replayMod.GetFromFile();
        
        UI.instance.AddMod(replayMod);
    }

    public void LoadReplayObjects()
    {
        GameObject ReplayTable = new GameObject("Replay Table");

        ReplayTable.transform.localPosition = new Vector3(5.9506f, 1.3564f, 4.1906f);
        ReplayTable.transform.localRotation = Quaternion.Euler(270f, 121.5819f, 0f);

        using var stream = typeof(Main).Assembly.GetManifestResourceStream("ReplayMod.src.Core.replayobjects2");
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
        replayNameText.transform.localScale = Vector3.one * 0.07f;
        replayNameText.transform.localPosition = new Vector3(0.446f, -0.0204f, -0.1297f);
        replayNameText.transform.localRotation = Quaternion.Euler(0, 249.3179f, 270f);

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
        timeline.transform.localPosition = new Vector3(0, -0.1091f, 0);
        timeline.transform.localScale = new Vector3(1.0715f, 0.0434f, 1);
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

        currentDuration.transform.localScale = Vector3.one * 0.8f;
        currentDuration.transform.localPosition = new Vector3(-0.2633f, -0.0412f, 0);
        currentDuration.transform.localRotation = Quaternion.identity;

        totalDuration.transform.SetParent(playbackControls.transform.GetChild(1));
        totalDuration.name = "Total Duration";

        totalDuration.transform.localScale = Vector3.one * 0.8f;
        totalDuration.transform.localPosition = new Vector3(0.696f, -0.0412f, 0);
        totalDuration.transform.localRotation = Quaternion.identity;

        playbackTitle.transform.SetParent(playbackControls.transform.GetChild(1));
        playbackTitle.name = "Playback Title";

        playbackTitle.transform.localScale = Vector3.one * 1.2f;
        playbackTitle.transform.localPosition = new Vector3(0, 0.5916f, 0);
        playbackTitle.transform.localRotation = Quaternion.identity;

        playbackTitle.horizontalAlignment = HorizontalAlignmentOptions.Center;

        playbackSpeedText.transform.SetParent(playbackControls.transform.GetChild(1));
        playbackSpeedText.name = "Playback Speed";

        playbackSpeedText.transform.localScale = Vector3.one * 1.3f;
        playbackSpeedText.transform.localPosition = new Vector3(0, 0.002f, 0);
        playbackSpeedText.transform.localRotation = Quaternion.identity;
        playbackSpeedText.horizontalAlignment = HorizontalAlignmentOptions.Center;

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
        p5x.transform.localPosition = new Vector3(0.1598f, -0.2665f, 0.096f);
        p5x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compnp5x = np5x.transform.GetChild(0).GetComponent<InteractionButton>();
        compnp5x.enabled = true;
        compnp5x.onPressed.RemoveAllListeners();
        compnp5x.onPressed.AddListener((UnityAction)(() => { Playback.AddPlaybackSpeed(-0.1f); }));
        compnp5x.transform.GetChild(3).GetComponent<MeshRenderer>().material.SetTexture("_Texture", speedUpTexture);

        np5x.name = "-0.1 Speed";
        np5x.transform.localScale = Vector3.one * 1.8f;
        np5x.transform.localPosition = new Vector3(-0.1598f, -0.2665f, 0.096f);
        np5x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compp1x = p1x.transform.GetChild(0).GetComponent<InteractionButton>();
        compp1x.enabled = true;
        compp1x.onPressed.RemoveAllListeners();
        compp1x.onPressed.AddListener((UnityAction)(() => { Playback.AddPlaybackSpeed(1f); }));

        p1x.name = "+1 Speed";
        p1x.transform.localScale = Vector3.one * 1.8f;
        p1x.transform.localPosition = new Vector3(0.31f, -0.2665f, 0.096f);
        p1x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compnp1x = np1x.transform.GetChild(0).GetComponent<InteractionButton>();
        compnp1x.enabled = true;
        compnp1x.onPressed.RemoveAllListeners();
        compnp1x.onPressed.AddListener((UnityAction)(() => { Playback.AddPlaybackSpeed(-1f); }));

        np1x.name = "-1 Speed";
        np1x.transform.localScale = Vector3.one * 1.8f;
        np1x.transform.localPosition = new Vector3(-0.31f, -0.2665f, 0.096f);
        np1x.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var compplay = playButton.transform.GetChild(0).GetComponent<InteractionButton>();
        compplay.enabled = true;
        compplay.onPressed.RemoveAllListeners();
        
        ReplayPlaybackControls.playButtonSprite = compplay.transform.GetChild(3)
            .GetComponent<MeshRenderer>();
        ReplayPlaybackControls.playSprite = ReplayPlaybackControls.playButtonSprite.material
            .GetTexture("_Texture") as Texture2D;
        
        compplay.onPressed.AddListener((UnityAction)(() => { Playback.TogglePlayback(Playback.isPaused); }));

        playButton.name = "Play Button";
        playButton.transform.localScale = Vector3.one * 2f;
        playButton.transform.localPosition = new Vector3(0, -0.2665f, 0.1156f);
        playButton.transform.localRotation = Quaternion.Euler(270, 0, 0);

        var textureP = bundle.LoadAsset<Texture2D>("Pause");
        ReplayPlaybackControls.pauseSprite = textureP;

        ReplayPlaybackControls.playButtonSprite.material.SetTexture("_Texture", ReplayPlaybackControls.pauseSprite);
        ReplayPlaybackControls.playButtonSprite.transform.localScale = Vector3.one * 0.8f;

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

        exitSceneButton.name = "Exit Scene";
        exitSceneButton.transform.localPosition = new Vector3(0.1527f, -0.6109f, 0f);
        exitSceneButton.transform.localScale = Vector3.one * 2f;
        exitSceneButton.transform.localRotation = Quaternion.identity;

        var exitSceneComp = exitSceneButton.transform.GetChild(0).GetComponent<InteractionButton>();
        exitSceneComp.enabled = true;
        exitSceneComp.onPressed.RemoveAllListeners();
        exitSceneComp.onPressed.AddListener((UnityAction)(() => { MelonCoroutines.Start(Utilities.LoadMap(1)); }));

        var exitSceneTMP = exitSceneButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        exitSceneTMP.text = "Exit Scene";
        exitSceneTMP.color = new Color(0.8f, 0, 0);
        exitSceneTMP.ForceMeshUpdate();

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
        GameObject.Destroy(replaySettingsPanel.transform.GetChild(6).gameObject);
        replaySettingsPanel.name = "Replay Settings";
        replaySettingsPanel.transform.localScale = Vector3.one;
        replaySettingsPanel.transform.GetChild(0).GetChild(0).gameObject.layer = LayerMask.NameToLayer("Default");
        
        for (int i = 0; i < replaySettingsPanel.transform.GetChild(1).childCount; i++)
            GameObject.Destroy(replaySettingsPanel.transform.GetChild(1).GetChild(i).gameObject);

        replaySettingsPanel.transform.localPosition = new Vector3(0.3782f, 0.88f, 0.1564f);
        replaySettingsPanel.transform.localRotation = Quaternion.Euler(34.4376f, 90, 90);
        
        var povCameraButton = GameObject.Instantiate(
            GameObjects.Gym.INTERACTABLES.DressingRoom.Controlpanel.Controls.Frameattachment.Viewoptions.ResetFighterButton.GetGameObject(),
            replaySettingsPanel.transform.GetChild(0)
        );

        povCameraButton.name = "POV Button";
        povCameraButton.transform.localPosition = new Vector3(-0.4193f, 0.0749f, -0.0164f);
        povCameraButton.transform.localRotation = Quaternion.identity;
        povCameraButton.transform.localScale = Vector3.one * 2f;
        
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
            replaySettingsPanel.transform.GetChild(0)
        );

        hideLocalPlayerButton.name = "Hide Local Player Toggle";
        hideLocalPlayerButton.transform.localPosition = new Vector3(-0.394f, -0.1135f, -0.00116f);
        hideLocalPlayerButton.transform.localRotation = Quaternion.identity;
        hideLocalPlayerButton.transform.localScale = Vector3.one * 1.6f;
        
        var hideLocalPlayerTMP =  hideLocalPlayerButton.transform.GetChild(1).GetComponent<TextMeshPro>();
        hideLocalPlayerTMP.text = "Hide Local Player";
        hideLocalPlayerTMP.transform.localScale = Vector3.one * 0.7f;
        
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
            replaySettingsPanel.transform.GetChild(0)
        );

        openControlsButton.name = "Open Controls Toggle";
        openControlsButton.transform.localPosition = new Vector3(0.3658f, -0.1428f, -0.0138f);
        openControlsButton.transform.localRotation = Quaternion.Euler(0, 0, 90);
        openControlsButton.transform.localScale = Vector3.one * 1.5f;

        var openControlsComp = openControlsButton.transform.GetChild(0).GetComponent<InteractionButton>();
        openControlsComp.enabled = true;
        openControlsComp.useLongPress = false;
        openControlsComp.onPressed.RemoveAllListeners();
        openControlsComp.onPressed.AddListener((UnityAction)(() =>
        {
            if (ReplayPlaybackControls.playbackControlsOpen) 
                ReplayPlaybackControls.Close();
            else 
                ReplayPlaybackControls.Open();
        }));

        var openControlsText = Create.NewText().GetComponent<TextMeshPro>();
        openControlsText.name = "Open Controls Text";
        openControlsText.transform.SetParent(openControlsComp.transform);
        openControlsText.transform.localPosition = new Vector3(0.0106f, 0.0117f, -0.1206f);
        openControlsText.transform.localRotation = Quaternion.Euler(90, 90, 0);
        openControlsText.transform.localScale = Vector3.one * 0.4f;

        openControlsText.text = ". . .";
        openControlsText.color = Color.white;
        openControlsText.ForceMeshUpdate();

        var slideOutPanel = GameObject.Instantiate(bundle.LoadAsset<GameObject>("SlideOutPlayerSelector"), replaySettingsPanel.transform.GetChild(0));

        slideOutPanel.name = "Player Selector Panel";
        slideOutPanel.transform.localScale = Vector3.one * 2.5f;
        slideOutPanel.transform.localPosition = new Vector3(0.1996f, 0.5273f, 0.16f);
        slideOutPanel.SetActive(false);

        var slideOutText = Create.NewText();

        slideOutText.transform.SetParent(slideOutPanel.transform);
        slideOutText.transform.localPosition = new Vector3(0.0022f, 0.072f, -0.004f);
        slideOutText.transform.localRotation = Quaternion.identity;
        slideOutText.transform.localScale = Vector3.one * 0.18f;

        var slideOutTextComp = slideOutText.GetComponent<TextMeshPro>();
        slideOutTextComp.text = "Player Selector";
        slideOutTextComp.color = new Color(0.1137f, 0.1059f, 0.0392f);
        slideOutTextComp.enableAutoSizing = true;
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
        pageNumberTextComp.ForceMeshUpdate();
        
        var timelineRS = GameObject.Instantiate(timeline, replaySettingsPanel.transform.GetChild(0));
        timelineRS.name = "Timeline";
        timelineRS.layer = LayerMask.NameToLayer("Default");
        timelineRS.transform.localPosition = new Vector3(0, -0.2726f, 0);
        timelineRS.transform.localScale = new Vector3(0.9346f, 0.0529f, 1.9291f);
        timelineRS.transform.localRotation = Quaternion.identity;
        
        var durationRS = GameObject.Instantiate(totalDuration, replaySettingsPanel.transform.GetChild(0));
        durationRS.name = "TotalDuration";
        durationRS.gameObject.layer = LayerMask.NameToLayer("Default");
        durationRS.transform.localPosition = new Vector3(0.2265f, -0.1898f, 0);
        
        var replayNameTitle = GameObject.Instantiate(playbackTitle.gameObject, replaySettingsPanel.transform.GetChild(0));
        replayNameTitle.transform.localPosition = new Vector3(0, 0.6316f, 0);
        replayNameTitle.name = "Replay Title";
        replayNameTitle.layer = LayerMask.NameToLayer("Default");
        
        var dateText = GameObject.Instantiate(playbackTitle.gameObject, replaySettingsPanel.transform.GetChild(0));
        dateText.transform.localPosition = new Vector3(0, 0.4989f, 0);
        dateText.transform.localScale = Vector3.one * 0.7f;
        dateText.name = "Date";
        dateText.layer = LayerMask.NameToLayer("Default");

        var deleteButton = GameObject.Instantiate(crystalizeButton, replaySettingsPanel.transform.GetChild(0));
        deleteButton.name = "DeleteReplay";
        deleteButton.transform.localPosition = new Vector3(0.21f, -0.5582f, -0.0138f);
        deleteButton.transform.localRotation = Quaternion.identity;
        deleteButton.transform.localScale = Vector3.one * 2f;

        var deleteButtonComp = deleteButton.transform.GetChild(0).GetComponent<InteractionButton>();

        deleteButtonComp.onPressedAudioCall = loadReplayButtonComp.onPressedAudioCall;
        
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

        var renameInstructions = GameObject.Instantiate(dateText, replaySettingsPanel.transform.GetChild(0));
        renameInstructions.name = "Rename Instructions";
        renameInstructions.transform.localPosition = new Vector3(0, 0.3156f, 0);
        renameInstructions.transform.localScale = Vector3.one;
        renameInstructions.SetActive(false);
        
        var renameInstructionsComp = renameInstructions.GetComponent<TextMeshPro>();
        renameInstructionsComp.text = "Please type on your keyboard to rename\n<#32a832>Enter - Confirm     <#cc1b1b>Esc - Cancel";
        
        var deleteReplayText = GameObject.Instantiate(renameInstructions, deleteButton.transform);
        deleteReplayText.name = "DeleteText";
        deleteReplayText.transform.localPosition = new Vector3(0.0143f, 0.0713f, 0);
        deleteReplayText.SetActive(true);
        deleteReplayText.transform.localScale = Vector3.one * 0.2f;
        
        var deleteReplayTextComp = deleteReplayText.GetComponent<TextMeshPro>();
        deleteReplayTextComp.text = "<#cc1b1b>DELETE REPLAY";
        
        var copyPathButton = GameObject.Instantiate(deleteButton, replaySettingsPanel.transform.GetChild(0));
        copyPathButton.name = "CopyPathButton";
        copyPathButton.transform.localPosition = new Vector3(-0.0337f, -0.5589f, -0.0138f);
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
        
        var renameButton = GameObject.Instantiate(deleteButton, replaySettingsPanel.transform.GetChild(0));
        renameButton.name = "RenameButton";
        renameButton.transform.localPosition = new Vector3(-0.2817f, -0.556f, -0.0138f);
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
        GameObject.Destroy(renameButton.transform.GetChild(0).GetChild(3).gameObject);
        
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
        HandleReplayPose();
        ReplayPlaybackControls.Update();

        if (currentScene != "Loader")
            ReplayCrystals.HandleCrystals();

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
        Recording.HandleRecording();
        Playback.HandlePlayback();
    }

    public override void OnFixedUpdate()
    {
        if (currentScene == "Loader") return;

        var rightActions = ((string)RightHandControls.SavedValue).Split(',').Select(a => a.Trim()).ToArray();
        var leftActions = ((string)LeftHandControls.SavedValue).Split(',').Select(a => a.Trim()).ToArray();
        
        TryHandleController(
            leftActions,
            Calls.ControllerMap.LeftController.GetPrimary(),
            Calls.ControllerMap.LeftController.GetSecondary(),
            true
        );
        
        TryHandleController(
            rightActions,
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
        string[] actions,
        float primary,
        float secondary,
        bool isLeft
    )
    {
        void PlayHaptics()
        {
            var haptics = LocalPlayer.Controller.GetSubsystem<PlayerHaptics>();
            
            if ((bool)EnableHaptics.SavedValue)
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

        foreach (var action in actions)
        {
            if (action.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;
            
            DebugLog($"Controller Action | {(isLeft ? "Left" : "Right")} | {action}");
            
            switch (action)
            {
                case "Toggle Recording":
                {
                    if (Recording.isRecording)
                        Recording.StopRecording();
                    else
                        Recording.StartRecording();

                    break;
                }
                
                case "Save Replay Buffer":
                {
                    Recording.SaveReplayBuffer();
                    PlayHaptics();
                    break;
                }

                case "Add Marker":
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
            targetY + (float)Main.instance.tableOffset.SavedValue, 
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