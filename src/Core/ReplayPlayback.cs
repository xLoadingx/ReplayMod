using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Networking;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using Il2CppRUMBLE.Poses;
using Il2CppRUMBLE.Utilities;
using MelonLoader;
using MelonLoader.Utils;
using ReplayMod.Replay;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using RumbleModdingAPI.RMAPI;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using AudioManager = Il2CppRUMBLE.Managers.AudioManager;
using EventType = ReplayMod.Replay.Serialization.EventType;
using Object = UnityEngine.Object;
using PlayerState = ReplayMod.Replay.Serialization.PlayerState;
using Random = UnityEngine.Random;
using Utilities = ReplayMod.Replay.Utilities;

namespace ReplayMod.Core;

/// <summary>
/// Represents a single replay playback context.
///
/// Loads replay data and reconstructs runtime state, and advances playback
/// through time via interpolation (see <see cref="HandlePlayback"/> in LateUpdate).
/// Instances are self-contained and may be run concurrently, if they are ticked independently.
/// </summary>
public class ReplayPlayback
{
    public ReplayRecording Recording;

    public ReplayPlayback(ReplayRecording recording = null)
    {
        Recording = recording ?? Main.Recording;
    }
    
    // Replay Control
    public ReplayInfo currentReplay;
    public bool isPlaying;
    public float playbackSpeed = 1f;
    
    public float elapsedPlaybackTime;
    public int currentPlaybackFrame;

    public static bool isReplayScene;

    public bool hasPaused;
    public bool isPaused;
    public float previousPlaybackSpeed = 1f;
    
    // Roots
    public GameObject ReplayRoot;
    public GameObject replayStructures;
    public GameObject replayPlayers;
    public GameObject pedestalsParent;
    public GameObject scenePropsParent;
    public GameObject VFXParent;
    
    // Structures
    public GameObject[] PlaybackStructures;
    public static HashSet<Structure> HiddenStructures = new();
    public PlaybackStructureState[] playbackStructureStates;
    public const bool disableBaseStructureSystems = true;

    // Players
    public Clone[] PlaybackPlayers;
    public PlaybackPlayerState[] playbackPlayerStates;
    public static Player povPlayer;
    
    // Player Pool
    public GameObject playerPoolRoot;
    public List<Clone> playerPool = new();

    // Pedestals
    public List<GameObject> replayPedestals = new();
    public PlaybackPedestalState[] playbackPedestalStates;
    
    // Scene Props
    public List<GameObject> replaySceneProps = new();
    
    // Events
    public int lastEventFrame = -1;
    
    public void HandlePlayback()
    {
        if (!isPlaying) return;

        elapsedPlaybackTime += Time.deltaTime * playbackSpeed;

        if (elapsedPlaybackTime >= currentReplay.Frames[^1].Time)
        {
            SetPlaybackTime(currentReplay.Frames[^1].Time);
            
            if (Main.instance.StopReplayWhenDone.Value)
                StopReplay();
            
            return;
        }
        
        if (elapsedPlaybackTime <= 0f)
        {
            SetPlaybackTime(0f);

            if (Main.instance.StopReplayWhenDone.Value)
                StopReplay();
            
            return;
        }
        
        SetPlaybackTime(elapsedPlaybackTime);

        ReplayPlaybackControls.timeline?.GetComponent<MeshRenderer>()?.material?.SetFloat("_BP_Current", elapsedPlaybackTime * 1000f);

        if (ReplayPlaybackControls.currentDuration != null)
        {
            TimeSpan t = TimeSpan.FromSeconds(elapsedPlaybackTime);
            
            ReplayPlaybackControls.currentDuration.text = t.TotalHours >= 1 ? 
                $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : 
                $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }
    }
    
    public void LoadReplay(string path, bool allowDifferentSceneLoad = false)
    {
        if (ReplayArchive.GetManifest(path).Scene != Main.currentScene && !allowDifferentSceneLoad)
        {
            Main.ReplayError("Replay attempted to load in wrong scene. Aborting.");
            return;
        }
        
        currentReplay = ReplaySerializer.LoadReplay(path);

        SetPlaybackSpeed(1f);
        
        ReplayRoot = new GameObject("Replay Root");
        pedestalsParent = new GameObject("Pedestals");
        replayPlayers = new GameObject("Replay Players");
        scenePropsParent = new GameObject("Replay Scene Props");
        
        VFXParent = new GameObject("Replay VFX");
        VFXParent.transform.SetParent(ReplayRoot.transform);
        
        elapsedPlaybackTime = 0f;
        currentPlaybackFrame = 0;
        
        // ------ Structures ------
        
        if (replayStructures != null) Object.Destroy(replayStructures);
        HiddenStructures.Clear();
        PlaybackStructures = null;
        foreach (var structure in CombatManager.instance.structures)
        {
            if (structure == null) continue; 
            
            structure.gameObject.SetActive(false);
            HiddenStructures.Add(structure);
        }

        foreach (var fruit in Object.FindObjectsOfType<Fruit>())
        {
            fruit.GetComponent<Collider>().enabled = false;
            fruit.GetComponent<Renderer>().enabled = false;
        }

        PlaybackStructures = new GameObject[currentReplay.Header.Structures.Length];
        replayStructures = new GameObject("Replay Structures");
        
        for (int i = 0; i < PlaybackStructures.Length; i++)
        {
            var headerStructure = currentReplay.Header.Structures[i];
            var pool = ReplayCache.structurePools.GetValueOrDefault(headerStructure.Type);

            if (pool == null)
            {
                Main.ReplayError($"Could not find pool for structure of type '{headerStructure.Type}'");
                return;
            }
            
            PlaybackStructures[i] = ReplayCache.structurePools.GetValueOrDefault(headerStructure.Type).FetchFromPool().gameObject;

            Structure structure = PlaybackStructures[i].GetComponent<Structure>();
            
            if (disableBaseStructureSystems)
            {
                structure.indistructable = true;
                structure.onBecameFreeAudio = null;
                structure.onBecameGroundedAudio = null;

                foreach (var col in structure.GetComponentsInChildren<Collider>())
                    col.enabled = false;

                var rb = PlaybackStructures[i].GetComponent<Rigidbody>();

                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.detectCollisions = false;
                }
                
                if (PlaybackStructures[i].TryGetComponent<NetworkGameObject>(out var networkGameObject))
                    Object.Destroy(networkGameObject);
            }
            
            if (currentReplay.Header.Structures[i].Type == StructureType.Target)
                structure.GetComponent<StructureTarget>().SetTargetBaseTierPointsAndUpdateVisuals(headerStructure.targetDamage ?? 1);
                
            if (Recording.isRecording || Recording.isBuffering)
                Recording.TryRegisterStructure(structure);
            
            PlaybackStructures[i].SetActive(false);
            PlaybackStructures[i].transform.SetParent(replayStructures.transform);
        }

        playbackStructureStates = new PlaybackStructureState[PlaybackStructures.Length];

        for (int i = 0; i < PlaybackStructures.Length; i++)
            playbackStructureStates[i] = new PlaybackStructureState();
        
        // ------ Players ------

        if (PlaybackPlayers != null)
        {
            foreach (var player in PlaybackPlayers)
            {
                if (player == null) continue;
                PlayerManager.instance.AllPlayers.Remove(player.Controller.assignedPlayer);
            }
        }
        
        PlaybackPlayers = null;
        
        // ------ Pedestals ------
        
        replayPedestals.Clear();
        
        for (int i = 0; i < currentReplay.Header.PedestalCount; i++)
        {
            var pedestal = PoolManager.instance.GetPool("MatchPedestal").FetchFromPool().gameObject;
            pedestal.transform.SetParent(pedestalsParent.transform);
            replayPedestals.Add(pedestal);
            
            if (currentReplay.Header.PedestalCount == 0)
                pedestal.SetActive(false);
        }
        
        playbackPedestalStates = new PlaybackPedestalState[replayPedestals.Count];
        
        // ------ Scene Props ------

        // replaySceneProps.Clear();
        //
        // foreach (var scenePropInfo in currentReplay.Header.SceneProps)
        // {
        //     var scenePropPool = scenePropInfo.type switch
        //     {
        //         ScenePropType.Fruit => PoolManager.instance.GetPool("Fruit"),
        //         _ => throw new Exception($"Unknown ScenePropType: {scenePropInfo.type}")
        //     };
        //
        //     if (scenePropPool == null) continue;
        //     
        //     var sceneProp = scenePropPool.FetchFromPool().gameObject;
        //     sceneProp.transform.SetParent(scenePropsParent.transform);
        //     replaySceneProps.Add(sceneProp);
        // }
        
        // --------------
        
        MelonCoroutines.Start(SpawnClones(() =>
        {
            replayStructures.transform.SetParent(ReplayRoot.transform);
            replayPlayers.transform.SetParent(ReplayRoot.transform);
            pedestalsParent.transform.SetParent(ReplayRoot.transform);
            scenePropsParent.transform.SetParent(ReplayRoot.transform);
        
            playbackPlayerStates = new PlaybackPlayerState[PlaybackPlayers.Length];

            for (int i = 0; i < PlaybackPlayers.Length; i++)
                playbackPlayerStates[i] = new PlaybackPlayerState();
        
            ReorderPlayers();

            try
            {
                if (currentReplay.Header.VoiceTrackInfos != null)
                {
                    foreach (var info in currentReplay.Header.VoiceTrackInfos)
                    {
                        var clone = PlaybackPlayers.FirstOrDefault(p =>
                            p.Controller.assignedPlayer.Data.GeneralData.PlayFabMasterId == info.MasterId);

                        if (clone == null) continue;

                        string voicePath = Path.Combine(currentReplay.Header.VoiceFolder, info.FileName);

                        var clip = ReplayVoices.LoadVoiceClipFromFile(voicePath);

                        if (clip == null) continue;

                        clone.VoiceTracks.Add(new Clone.VoiceTrack
                        {
                            StartTime = info.StartTime, Clip = clip
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Main.ReplayError($"Voice laoding failed: {e}");
            }
            
            if (currentReplay.Header.Scene == "Gym")
            {
                foreach (var playbackPlayer in PlaybackPlayers)
                {
                    playbackPlayer.Controller.transform.GetChild(6).gameObject.SetActive(false);
                    playbackPlayer.Controller.transform.GetChild(9).gameObject.SetActive(false);
                    playbackPlayer.Controller.transform.position = Vector3.zero;
                }
            }
        
            isPlaying = true;
            TogglePlayback(true);

            ReplayRoot.transform.position = Vector3.zero;

            MelonCoroutines.Start(DisableNameplate());
            
            IEnumerator DisableNameplate()
            {
                yield return null;
                Main.LocalPlayer?.Controller?.transform.GetChild(6).gameObject.SetActive(false);
            }
            
            ReplayAPI.ReplayStartedInternal(currentReplay);
        }));
        
        // Playback Controls
        var timelineRenderer = ReplayPlaybackControls.timeline.GetComponent<MeshRenderer>();
        
        timelineRenderer.material.SetFloat("_BP_Target", currentReplay.Header.Duration * 1000f);
        timelineRenderer.material.SetFloat("_BP_Current", 0f);

        TimeSpan t = TimeSpan.FromSeconds(currentReplay.Header.Duration);

        ReplayPlaybackControls.totalDuration.text = t.TotalHours >= 1 ? 
            $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : 
            $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        
        ReplayPlaybackControls.currentDuration.text = "0:00";

        ReplayPlaybackControls.playbackTitle.text = Path.GetFileNameWithoutExtension(path).StartsWith("Replay") 
            ? currentReplay.Header.Title 
            : Path.GetFileNameWithoutExtension(path);

        ReplaySettings.playerList = ReplaySettings.PaginateReplay(currentReplay.Header, PlaybackPlayers);
        ReplaySettings.SelectPlayerPage(0);
        ReplaySettings.povButton.SetActive(true);
        ReplaySettings.hideLocalPlayerToggle.SetActive(true);
        ReplaySettings.openControlsButton.SetActive(true);
        
        ReplayPlaybackControls.timeline.transform.GetChild(0).GetComponent<ReplaySettings.TimelineScrubber>().header = currentReplay.Header;

        Utilities.AddMarkers(currentReplay.Header, timelineRenderer);
    }
    
    public void StopReplay()
    {
        if (!isPlaying) return;
        isPlaying = false;
        ReplayPlaybackControls.Close();
        
        string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "ReplayMod", "TempReplayVoices");
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
        
        UpdateReplayCameraPOV(Main.LocalPlayer);
        TogglePlayback(true, ignoreIsPlaying: true);
        SetPlaybackSpeed(1f);

        foreach (var structure in PlaybackStructures)
        {
            if (structure == null) continue;

            var comp = structure.GetComponent<Structure>();
            comp.indistructable = false;
            
            if (comp.currentFrictionVFX != null)
                Object.Destroy(comp.currentFrictionVFX.gameObject);

            foreach (var effect in structure.GetComponentsInChildren<VisualEffect>())
                Object.Destroy(effect);
        }

        foreach (var structure in HiddenStructures)
        {
            if (structure != null)
                structure.gameObject.SetActive(true);
        }

        foreach (var fruit in Object.FindObjectsOfType<Fruit>(true))
        {
            fruit.GetComponent<Collider>().enabled = true;
            fruit.GetComponent<Renderer>().enabled = true;
        }
        
        HiddenStructures.Clear();
        
        if (replayStructures != null)
            Object.Destroy(replayStructures);

        foreach (var player in PlaybackPlayers)
        {
            if (player == null) continue;
            
            PlayerManager.instance.AllPlayers.Remove(player.Controller.assignedPlayer);

            player.gameObject.SetActive(false);
            player.Controller.transform.SetParent(playerPoolRoot.transform);
        }
        
        if (replayPlayers != null)
            Object.Destroy(replayPlayers);

        if (pedestalsParent != null)
        {
            for (int i = pedestalsParent.transform.childCount - 1; i >= 0; i--)
            {
                var pedestal = pedestalsParent.transform.GetChild(i);
                Object.Destroy(pedestal.gameObject);
            }
            Object.Destroy(pedestalsParent);
        }

        if (scenePropsParent != null)
        {
            for (int i = scenePropsParent.transform.childCount - 1; i >= 0; i--)
            {
                var sceneProp =  scenePropsParent.transform.GetChild(i);
                Object.Destroy(sceneProp.gameObject);
            }
            Object.Destroy(scenePropsParent);
        }

        if (ReplayRoot != null)
            Object.Destroy(ReplayRoot);
        
        ReplaySettings.povButton.SetActive(false);
        ReplaySettings.hideLocalPlayerToggle.SetActive(false);
        ReplaySettings.openControlsButton.SetActive(false); 

        replayStructures = null;
        replayPlayers = null;
        PlaybackStructures = null;
        PlaybackPlayers = null;
        
        Main.LocalPlayer.Controller.PlayerNameTag.gameObject.SetActive(false);

        ReplayAPI.ReplayEndedInternal(currentReplay);
    }
    
    public void ReorderPlayers()
    {
        var all = PlayerManager.instance.AllPlayers;
        if (all == null || all.Count == 0)
            return;

        var wasHostById = new Dictionary<string, bool>();
        foreach (var p in currentReplay.Header.Players)
        {
            if (p == null) continue;
            wasHostById[p.MasterId] = p.WasHost;
        }
        
        Player host = null;
        Player local = Main.LocalPlayer;
        var middle = new List<Player>();

        foreach (var p in all)
        {
            if (p == local)
                continue;

            if (wasHostById.TryGetValue(p.Data.GeneralData.PlayFabMasterId, out bool wasHost) && wasHost)
                host = p;
            else
                middle.Add(p);
        }

        all.Clear();

        if (host != null)
            all.Add(host);

        foreach (var p in middle)
            all.Add(p);

        if (local != null)
            all.Add(local);
    }
    
    private IEnumerator SpawnClones(Action done = null)
    {
        int count = currentReplay.Header.Players.Length;
        PlaybackPlayers = new Clone[count];
    
        var frame0 = currentReplay.Frames[0];
    
        // Non-greedy pooling algorithm
        var chosen = new Clone[count];
        var used = new HashSet<Clone>();
        
        // Exact visual matches
        for (int i = 0; i < count; i++)
        {
            var visual = frame0.Players[i]?.visualData;
            if (visual == null) continue;
    
            foreach (var c in playerPool)
            {
                if (!used.Contains(c) && c.Controller.assignedPlayer.Data.VisualData.ToPlayfabDataString() == visual)
                {
                    chosen[i] = c;
                    used.Add(c);
                    break;
                }
            }
        }
    
        // Fill the remaining players who
        // didn't have an exact match in visuals
        for (int i = 0; i < count; i++)
        {
            if (chosen[i] != null) continue;
    
            foreach (var c in playerPool)
            {
                if (!used.Contains(c))
                {
                    chosen[i] = c;
                    used.Add(c);
                    break;
                }
            }
        }
        
        // Assignment
        for (int i = 0; i < count; i++)
        {
            var pInfo = currentReplay.Header.Players[i];
    
            if (pInfo == null)
            {
                Main.DebugLog($"Header.Players[{i}] is null.");
                continue;
            }
    
            Clone temp = chosen[i];
    
            // Build new players if count
            // is higher than the amount of pooled players
            if (temp == null)
            {
                MelonCoroutines.Start(BuildClone(pInfo, c => temp = c));
    
                while (temp == null)
                    yield return null;
    
                playerPool.Add(temp);
            }

            BindCloneToPlayerInfo(temp, pInfo);

            temp.VoiceTracks.Clear();
            temp.currentTrack = null;

            if (temp.VoiceSource != null)
            {
                temp.VoiceSource.Stop();
                temp.VoiceSource.clip = null;
                temp.VoiceSource.time = 0f;
                temp.VoiceSource.pitch = 1f;
            }
            
            PlayerManager.instance.AllPlayers.Add(temp.Controller.assignedPlayer);
    
            temp.gameObject.SetActive(true);
            PlaybackPlayers[i] = temp;
            temp.Controller.transform.SetParent(replayPlayers.transform);
        }
    
        done?.Invoke();
    }
    
    // private IEnumerator SpawnClones(Action done = null)
    // {
    //     int count = currentReplay.Header.Players.Length;
    //     PlaybackPlayers = new Clone[count];
    //
    //     for (int i = 0; i < count; i++)
    //     {
    //         var pInfo = currentReplay.Header.Players[i];
    //
    //         if (pInfo == null)
    //         {
    //             Main.DebugLog($"Header.Players[{i}] is null.");
    //             continue;
    //         }
    //
    //         Clone temp = null;
    //
    //         MelonCoroutines.Start(BuildClone(pInfo, c => temp = c));
    //
    //         while (temp == null)
    //             yield return null;
    //
    //         PlayerManager.instance.AllPlayers.Add(temp.Controller.assignedPlayer);
    //
    //         temp.gameObject.SetActive(true);
    //         PlaybackPlayers[i] = temp;
    //         temp.Controller.transform.SetParent(replayPlayers.transform);
    //     }
    //
    //     done?.Invoke();
    // }
    
    public static IEnumerator BuildClone(PlayerInfo pInfo, Action<Clone> callback, Vector3 initialPosition = default)
    {
        var randomID = Guid.NewGuid().ToString();
        
        pInfo.Name = string.IsNullOrEmpty(pInfo.Name) ? $"Player_{pInfo.MasterId}" : pInfo.Name;
        
        pInfo.EquippedShiftStones ??= new short[] { -1, -1 };
        var shiftstones = new Il2CppStructArray<short>(2);
        
        for (int i = 0; i < 2; i++)
            shiftstones[i] = pInfo.EquippedShiftStones[i];
        
        PlayerData data = new PlayerData(
            new GeneralData
            {
                PlayFabMasterId = $"{pInfo.MasterId}",
                PlayFabTitleId = randomID,
                BattlePoints = pInfo.BattlePoints,
                PublicUsername = pInfo.Name
            },
            Main.LocalPlayer.Data.RedeemedMoves,
            Main.LocalPlayer.Data.EconomyData,
            shiftstones,
            PlayerVisualData.Default
        ); 
        
        data.PlayerMeasurement = pInfo.Measurement.Length != 0 ? pInfo.Measurement : Main.LocalPlayer.Data.PlayerMeasurement;

        Player newPlayer = Player.CreateRemotePlayer(data);
        PlayerManager.instance.SpawnPlayerController(newPlayer, Vector3.zero, Quaternion.identity);

        while (newPlayer.Controller == null)
            yield return null;

        GameObject body = newPlayer.Controller.gameObject;
        
        body.name = $"Player_{pInfo.MasterId}";
        
        GameObject Overall = body.transform.GetChild(2).gameObject;
        GameObject LHand = Overall.transform.GetChild(1).gameObject;
        GameObject RHand = Overall.transform.GetChild(2).gameObject;
        GameObject Head = Overall.transform.GetChild(0).GetChild(0).gameObject;

        Object.Destroy(Overall.GetComponent<NetworkGameObject>());
        Object.Destroy(Overall.GetComponent<PlayerSessionStateSystem>());
        newPlayer.Controller.PlayerAnimator.animator.SetBool(-414412114, false);
        Object.Destroy(Overall.GetComponent<Rigidbody>());
        
        var localTransform = Main.LocalPlayer.Controller.transform;
        newPlayer.Controller.transform.position = localTransform.position;
        newPlayer.Controller.transform.rotation = localTransform.rotation;

        var physics = body.transform.GetChild(3);
        var lControllerPhysics = physics.GetChild(2).GetComponent<ConfigurableJoint>();
        lControllerPhysics.xMotion = 0;
        lControllerPhysics.yMotion = 0;
        lControllerPhysics.zMotion = 0;

        var rControllerPhysics = physics.GetChild(3).GetComponent<ConfigurableJoint>();
        rControllerPhysics.xMotion = 0;
        rControllerPhysics.yMotion = 0;
        rControllerPhysics.zMotion = 0;
        
        foreach (var driver in body.GetComponentsInChildren<TrackedPoseDriver>())
            driver.enabled = false;
        
        body.transform.GetChild(6).gameObject.SetActive(false);

        var poseSystem = body.GetComponent<PlayerPoseSystem>();
        foreach (var pose in Main.LocalPlayer.Controller.PlayerPoseSystem.currentInputPoses)
            poseSystem.currentInputPoses.Add(new PoseInputSource(pose.PoseSet));
        poseSystem.enabled = true;

        var clone = newPlayer.Controller.gameObject.AddComponent<Clone>();

        newPlayer.Controller.PlayerNameTag.gameObject.SetActive(Main.instance.ToggleNameplate.Value);
        newPlayer.Controller.PlayerHealth.transform.GetChild(1).gameObject.SetActive(Main.instance.ToggleHealthBar.Value);
        newPlayer.Controller.gameObject.GetComponentInChildren<PlayerUIBar>().InitializeMaterials(newPlayer.Controller, PlayerUIBar.UIBarMode.Health);

        clone.VRRig = Overall;
        clone.LeftHand = LHand;
        clone.RightHand = RHand;
        clone.Head = Head;
        clone.Controller = newPlayer.Controller;
        
        body.transform.position = initialPosition;
        body.transform.rotation = Quaternion.identity;

        callback?.Invoke(clone);
    }

    private static void BindCloneToPlayerInfo(Clone clone, PlayerInfo pInfo)
    {
        pInfo.Name = string.IsNullOrEmpty(pInfo.Name)
            ? $"Player_{pInfo.MasterId}"
            : pInfo.Name;
        
        pInfo.EquippedShiftStones ??= new short[] { -1, -1 };

        var player = clone.Controller.assignedPlayer;
        var data = player.Data;

        data.GeneralData.PlayFabMasterId = pInfo.MasterId;
        data.GeneralData.PlayFabTitleId = Guid.NewGuid().ToString();
        data.GeneralData.BattlePoints = pInfo.BattlePoints;
        data.GeneralData.PublicUsername = pInfo.Name;

        data.PlayerMeasurement = pInfo.Measurement.Length != 0
            ? pInfo.Measurement
            : Main.LocalPlayer.Data.PlayerMeasurement;

        var shiftstones = new Il2CppStructArray<short>(2);
        shiftstones[0] = pInfo.EquippedShiftStones.Length > 0 ? pInfo.EquippedShiftStones[0] : (short)-1;
        shiftstones[1] = pInfo.EquippedShiftStones.Length > 1 ? pInfo.EquippedShiftStones[1] : (short)-1;

        data.EquipedShiftStones = shiftstones;

        clone.Controller.gameObject.name = $"Player_{pInfo.MasterId}";
        
        clone.Controller.PlayerNameTag.RefreshNameTag();
    }
    
    public void ApplyInterpolatedFrame(int frameIndex, float t)
    {
        var frames = currentReplay.Frames;

        if (replayPlayers?.activeSelf == false || replayStructures?.activeSelf == false)
            return;

        if ((playbackSpeed > 0 && frameIndex >= frames.Length - 1) ||
            (playbackSpeed < 0 && frameIndex <= 0))
            return;
        
        // Interpolation
        t = playbackSpeed >= 0 ? t : 1f - t;
        Frame a = frames[frameIndex];
        Frame b = frames[frameIndex + (playbackSpeed >= 0 ? 1 : -1)];
        
        var poolManager = PoolManager.instance;

        // ------ Structures ------

        for (int i = 0; i < PlaybackStructures.Length; i++)
        {
            var playbackStructure = PlaybackStructures[i];
            var structureComp = playbackStructure.GetComponent<Structure>();
            var sa = a.Structures[i];
            var sb = b.Structures[i];

            ref var state = ref playbackStructureStates[i];

            foreach (var collider in playbackStructure.GetComponentsInChildren<Collider>())
                collider.enabled = false;

            // ------ State Event Checks ------

            // Structure Broke
            if (state.active && !sb.active)
            {
                AudioManager.instance.Play(
                    playbackStructure.GetComponent<Structure>().onDeathAudio,
                    playbackStructure.transform.position
                );

                try {
                    structureComp.onStructureDestroyed?.Invoke();
                } catch { }
                
                if (structureComp.currentFrictionVFX != null)
                    Object.Destroy(structureComp.currentFrictionVFX.gameObject);
            }
            
            // Structure Spawned
            if (!state.active && sb.active && frameIndex != 0)
            {
                string sfx = playbackStructure.name switch
                {
                    "Wall" => "Call_Structure_Spawn_Heavy",
                    "Ball" or "Disc" => "Call_Structure_Spawn_Light",
                    "LargeRock" => "Call_Structure_Spawn_Massive",
                    _ => "Call_Structure_Spawn_Medium"
                };

                if (ReplayCache.SFX.TryGetValue(sfx, out var audioCall) && frameIndex + 2 < frames.Length)
                    AudioManager.instance.Play(audioCall, frames[frameIndex + 2].Structures[i].position);
                
                foreach (var visualEffect in playbackStructure.GetComponentsInChildren<PooledVisualEffect>())
                    Object.Destroy(visualEffect.gameObject);
                
                structureComp.OnFetchFromPool();
            }
            
            state.active = sb.active;
            playbackStructure.SetActive(state.active);

            // States
            if (structureComp.currentPhysicsState != sb.currentState)
            {
                state.currentState = sb.currentState;
                structureComp.currentPhysicsState = sb.currentState;

                bool parryFromHistop = sa.currentState == Structure.PhysicsState.Frozen && sb.currentState == Structure.PhysicsState.Floating;

                // Hitstop
                bool isHitstop = state.currentState == Structure.PhysicsState.Frozen;
                var renderer = structureComp.transform.GetComponentInChildren<Renderer>();
                renderer?.material?.SetFloat("_shake", isHitstop ? 1 : 0);
                renderer?.material?.SetFloat("_shakeFrequency", 75 * playbackSpeed);
                
                // Parry
                bool isParried = state.currentState == Structure.PhysicsState.Floating;
                if (!parryFromHistop)
                {
                    if (isParried)
                    {
                        SpawnVFX(
                            "Parry_VFX",
                            VFXParent.transform,
                            sa.position,
                            Quaternion.identity,
                            "StructureParry",
                            structureComp,
                            true
                        );

                        AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Parry"], sa.position);
                    }
                    else
                    {
                        var tag = VFXParent.GetComponentsInChildren<ReplayTag>().FirstOrDefault(tag => tag.Type == "StructureParry" && tag.attachedStructure == structureComp);
                    
                        if (tag != null)
                            Object.Destroy(tag.gameObject);
                    }
                }
            }
            
            // Hold started
            if (!state.isLeftHeld && sb.isLeftHeld)
                SpawnHoldVFX("Left");
            
            if (!state.isRightHeld && sb.isRightHeld)
                SpawnHoldVFX("Right");

            void SpawnHoldVFX(string hand)
            {
                SpawnVFX(
                    "Hold_VFX",
                    playbackStructure.transform,
                    Vector3.zero,
                    Quaternion.identity,
                    "StructureHold_" + hand,
                    structureComp
                );

                AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Hold"], sa.position);
            }

            // Hold ended
            if (state.isLeftHeld && !sb.isLeftHeld)
                DestroyHoldVFX("Left");

            if (state.isRightHeld && !sb.isRightHeld)
                DestroyHoldVFX("Right");

            void DestroyHoldVFX(string hand)
            {
                foreach (var vfx in playbackStructure.GetComponentsInChildren<ReplayTag>())
                {
                    if (vfx == null)
                        continue;

                    if (vfx.Type == "StructureHold_" + hand && vfx.transform.parent == playbackStructure.transform)
                    {
                        Object.Destroy(vfx.gameObject);
                        break;
                    }
                }
            }

            state.isLeftHeld = Utilities.HasVFXType("StructureHold_Left", playbackStructure.transform);
            state.isRightHeld = Utilities.HasVFXType("StructureHold_Right", playbackStructure.transform);

            // Flick started
            if (!state.isFlicked && sb.isFlicked)
            {
                SpawnVFX(
                    "Flick_VFX",
                    playbackStructure.transform,
                    Vector3.zero,
                    Quaternion.identity,
                    "StructureFlick",
                    structureComp
                );

                AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Flick"], sa.position);
            }

            // Flick ended
            if (state.isFlicked && !sb.isFlicked)
            {
                foreach (var vfx in playbackStructure.GetComponentsInChildren<ReplayTag>())
                {
                    if (vfx == null)
                        continue;

                    if (vfx.name.Contains("Flick_VFX") && vfx.transform.parent == playbackStructure.transform)
                    {
                        Object.Destroy(vfx.gameObject);
                        break;
                    }
                }
            }

            state.isFlicked = Utilities.HasVFXType("StructureFlick", playbackStructure.transform);

            // ------------
            
            if (sa.active && sb.active)
            {
                Vector3 pos = Vector3.Lerp(sa.position, sb.position, t);
                Quaternion rot = Quaternion.Slerp(sa.rotation, sb.rotation, t);

                playbackStructure.transform.SetLocalPositionAndRotation(pos, rot);
            }
            else
            {
                playbackStructure.transform.SetLocalPositionAndRotation(sb.position, sb.rotation);
            }

            foreach (var vfx in playbackStructure.GetComponentsInChildren<VisualEffect>())
            {
                vfx.playRate = Abs(playbackSpeed);

                if (vfx.GetComponent<FrameControlledVFX>() == null)
                    vfx.gameObject.AddComponent<FrameControlledVFX>();

                if (vfx.name.Contains("ExplodeStatus_VFX"))
                    vfx.transform.localScale = Vector3.one;
            }
            
            var frictionController = playbackStructure.GetComponent<FrictionController>();

            if (frictionController == null)
                frictionController = playbackStructure.AddComponent<FrictionController>();
            
            Vector3 velocity = (sb.position - sa.position) / (b.Time - a.Time);
            velocity.y = 0f; // ignores vertical movement as structures only move up whilst grounded when spawning

            bool grounded = structureComp.currentPhysicsState is Structure.PhysicsState.StableGrounded 
                or Structure.PhysicsState.FreeGrounded;

            if (sa.active && sb.active)
            {
                frictionController.Evaluate(
                    Time.deltaTime * playbackSpeed, // kinda bad, but too much of an edge case to care about
                    grounded,
                    velocity
                );
            }
            
            if (structureComp.currentFrictionVFX != null)
                structureComp.currentFrictionVFX.visualEffect.playRate = Abs(playbackSpeed);
        }

        // ------ Players ------

        for (int i = 0; i < PlaybackPlayers.Length; i++)
        {
            var playbackPlayer = PlaybackPlayers[i];
            if (playbackPlayer == null) continue;
            
            var pa = a.Players[i];
            var pb = b.Players[i];

            ref var state = ref playbackPlayerStates[i];

            if (state.health != pb.Health)
                playbackPlayer.Controller.PlayerHealth.SetHealth(pb.Health, (short)state.health);
            
            state.health = playbackPlayer.Controller.assignedPlayer.Data.HealthPoints;
            
            if (state.currentStack != pb.currentStack)
            { 
                state.currentStack = pb.currentStack;

                if (pb.currentStack != (short)StackType.Flick
                    && pb.currentStack != (short)StackType.HoldLeft
                    && pb.currentStack != (short)StackType.HoldRight
                    && pb.currentStack != (short)StackType.Ground
                    && pb.currentStack != (short)StackType.Parry
                    && pb.currentStack != (short)StackType.None)
                {
                    var key = ReplayCache.NameToStackType
                        .FirstOrDefault(s => s.Value == (StackType)pb.currentStack);

                    var stack = playbackPlayer.Controller
                        .PlayerProcessor
                        .availableStacks
                        .ToArray()
                        .FirstOrDefault(s => s.CachedName == key.Key);

                    if (stack != null)
                    {
                        playbackPlayer.Controller
                            .PlayerProcessor
                            .Execute(stack);

                        if (pb.currentStack == (short)StackType.Dash)
                            playbackPlayer.lastDashTime = elapsedPlaybackTime;

                        if (pb.currentStack == (short)StackType.Jump)
                            AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Jump"], 
                                playbackPlayer.Controller.PlayerVR.headset.Transform.position);
                    }
                }
            }

            if (state.activeShiftstoneVFX != pb.activeShiftstoneVFX)
            {
                state.activeShiftstoneVFX = pb.activeShiftstoneVFX;
                var chest = playbackPlayer.Controller.PlayerIK.VrIK.references.chest;
                var flags = state.activeShiftstoneVFX;

                TryToggleVFX("Chargestone VFX", PlayerShiftstoneVFX.Charge);
                TryToggleVFX("Adamantstone_VFX", PlayerShiftstoneVFX.Adamant);
                TryToggleVFX("Surgestone_VFX", PlayerShiftstoneVFX.Surge);
                TryToggleVFX("Vigorstone_VFX", PlayerShiftstoneVFX.Vigor);
                
                void TryToggleVFX(string name, PlayerShiftstoneVFX flag)
                {
                    var vfx = chest.Find(name)?.GetComponent<VisualEffect>();
                    if (vfx == null) return;

                    string shiftstoneName = name switch
                    {
                        "Chargestone VFX" => "ChargeStone",
                        "Surgestone_VFX" => "SurgeStone",
                        "Vigorstone_VFX" => "VigorStone",
                        "Adamantstone_VFX" => "AdamantStone",
                        _ => ""
                    };
                    
                    if (flags.HasFlag(flag))
                    {
                        vfx.transform.localScale = Vector3.one;
                        
                        vfx.Play();
                        vfx.playRate = Abs(playbackSpeed);
                        AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_Use"], chest.transform.position);
                        var socketIndex = playbackPlayer.Controller.PlayerShiftstones.shiftStoneSockets
                            .FirstOrDefault(s => s.assignedShifstone.name == shiftstoneName)?.assignedSocketIndex;

                        if (socketIndex.HasValue)
                            playbackPlayer.Controller.PlayerShiftstones
                                .ActivateUseShiftstoneEffects(socketIndex == 0 ? InputManager.Hand.Left : InputManager.Hand.Right);
                    }
                    else
                    {
                        vfx.Stop();
                    }
                }
            }

            var shiftstones = playbackPlayer.Controller.PlayerShiftstones.GetCurrentShiftStoneConfiguration();
            int currentLeft = shiftstones.Count > 0 ? shiftstones[0] : -1;
            int currentRight = shiftstones.Count > 1 ? shiftstones[1] : -1;
            if (currentLeft != pb.leftShiftstone)
            {
                state.leftShiftstone = pb.leftShiftstone;
                ApplyShiftstone(playbackPlayer.Controller, 0, pb.leftShiftstone);
            }
            
            if (currentRight != pb.rightShiftstone)
            {
                state.rightShiftstone = pb.rightShiftstone;
                ApplyShiftstone(playbackPlayer.Controller, 1, pb.rightShiftstone);
            }
            
            void ApplyShiftstone(PlayerController controller, int socketIndex, int shiftstoneIndex)
            {
                var shiftstoneSystem = controller.PlayerShiftstones;

                shiftstoneSystem.RemoveShiftStone(socketIndex, false);
                
                PooledMonoBehaviour pooledObject = shiftstoneIndex switch
                {
                    0 => poolManager.GetPooledObject("AdamantStone"),
                    1 => poolManager.GetPooledObject("ChargeStone"),
                    2 => poolManager.GetPooledObject("FlowStone"),
                    3 => poolManager.GetPooledObject("GuardStone"),
                    4 => poolManager.GetPooledObject("StubbornStone"),
                    5 => poolManager.GetPooledObject("SurgeStone"),
                    6 => poolManager.GetPooledObject("VigorStone"),
                    7 => poolManager.GetPooledObject("VolatileStone"),
                    _ => null
                };
            
                if (pooledObject == null)
                {
                    shiftstoneSystem.RemoveShiftStone(socketIndex, false);
                    return;
                }
            
                shiftstoneSystem.AttachShiftStone(pooledObject.GetComponent<ShiftStone>(), socketIndex, false, false);
                AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_EquipBoth"], pooledObject.transform.position);
            }

            var rockCam = playbackPlayer.Controller.PlayerLIV?.LckTablet;
            if (rockCam != null && Main.instance.ToggleRockCam.Value)
            {
                if (state.rockCamActive != pb.rockCamActive)
                {
                    state.rockCamActive = pb.rockCamActive;
                    rockCam.transform.SetParent(ReplayRoot.transform);
                    rockCam.name = playbackPlayer.name + "_RockCam";
                    rockCam.gameObject.SetActive(state.rockCamActive);
                    
                    for (int j = 0; j < rockCam.transform.childCount; j++)
                        rockCam.transform.GetChild(j).gameObject.SetActive(state.rockCamActive);
                }
                
                Vector3 pos = Vector3.Lerp(pa.rockCamPos, pb.rockCamPos, t);
                Quaternion rot = Quaternion.Slerp(pa.rockCamRot, pb.rockCamRot, t);
                
                rockCam.transform.SetLocalPositionAndRotation(pos, rot);
            }

            var current = playbackPlayer.Controller.assignedPlayer.Data.PlayerMeasurement;
            if (!Approximately(state.playerMeasurement.ArmSpan, pb.ArmSpan) || !Approximately(state.playerMeasurement.Length, pb.Length) ||
                !Approximately(current.ArmSpan, pb.ArmSpan) || !Approximately(current.Length, pb.Length))
            {
                var measurement = new PlayerMeasurement(pb.Length, pb.ArmSpan);
                state.playerMeasurement = measurement;

                playbackPlayer.Controller.PlayerScaling.ScaleController(measurement);
                playbackPlayer.Controller.assignedPlayer.Data.PlayerMeasurement = measurement;

                UpdateReplayCameraPOV(povPlayer ?? Main.LocalPlayer, ReplaySettings.hideLocalPlayer);

                AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Succes"], 
                    playbackPlayer.Controller.PlayerIK.VrIK.references.head.position
                );
            }

            if ((!string.Equals(state.visualData, pb.visualData) && !string.IsNullOrEmpty(pb.visualData)) || playbackPlayer.Controller.PlayerVisuals.renderer.sharedMesh == null)
            {
                if (pb.visualData != null)
                {
                    var newVisualData = PlayerVisualData.FromPlayfabDataString(pb.visualData);
                    state.visualData = pb.visualData;
                    
                    playbackPlayer.Controller.assignedPlayer.Data.VisualData = newVisualData;
                    playbackPlayer.Controller.Initialize(playbackPlayer.Controller.assignedPlayer);

                    playbackPlayer.Controller.transform.GetChild(9).gameObject.SetActive(false);
                }
            }
            
            playbackPlayer.Controller.PlayerNameTag.gameObject.SetActive(
                Main.instance.ToggleNameplate.Value && povPlayer != playbackPlayer.Controller.assignedPlayer
            );
            
            playbackPlayer.Controller.PlayerHealth.transform.GetChild(1).gameObject.SetActive(
                Main.instance.ToggleHealthBar.Value && povPlayer != playbackPlayer.Controller.assignedPlayer
            );
            
            if (state.active != pb.active)
                playbackPlayer.Controller.gameObject.SetActive(pb.active);
            
            state.active = playbackPlayer.Controller.gameObject.activeSelf;

            var lHandPresence = PlayerHandPresence.HandPresenceInput.Empty;
            lHandPresence.gripInput = pa.lgripInput;
            lHandPresence.thumbInput = pa.lthumbInput;
            lHandPresence.indexInput = pa.lindexInput;
            playbackPlayer.lHandInput = lHandPresence;
            
            var rHandPresence = PlayerHandPresence.HandPresenceInput.Empty;
            rHandPresence.gripInput = pa.rgripInput;
            rHandPresence.thumbInput = pa.rthumbInput;
            rHandPresence.indexInput = pa.rindexInput;
            playbackPlayer.rHandInput = rHandPresence;
            
            playbackPlayer.ApplyInterpolatedPose(pa, pb, t);

            foreach (var vfx in playbackPlayer.GetComponentsInChildren<VisualEffect>())
            {
                vfx.playRate = Abs(playbackSpeed);

                if (vfx.GetComponent<FrameControlledVFX>() == null)
                    vfx.gameObject.AddComponent<FrameControlledVFX>();
            }
        }

        // ------ Pedestals ------

        if (replayPedestals.Count >= currentReplay.Header.PedestalCount)
        {
            for (int i = 0; i < currentReplay.Header.PedestalCount; i++)
            {
                var playbackPedestal = replayPedestals[i];
                var pa = a.Pedestals[i];
                var pb = b.Pedestals[i];

                ref var state = ref playbackPedestalStates[i];

                if (state.active != pb.active)
                    playbackPedestal.SetActive(pb.active);

                state.active = playbackPedestal.activeSelf;

                Vector3 pos = Vector3.Lerp(pa.position, pb.position, t);
                playbackPedestal.transform.localPosition = pos;
            }
        }
        
        // ------ Scene Props ------

        if (replaySceneProps.Count >= currentReplay.Header.ScenePropCount)
        {
            for (int i = 0; i < currentReplay.Header.ScenePropCount; i++)
            {
                var playbackSceneProp = replaySceneProps[i];
                var pa = a.SceneProps[i];
                var pb = b.SceneProps[i];
                
                Vector3 pos = Vector3.Lerp(pa.position, pb.position, t);
                Quaternion rot = Quaternion.Slerp(pa.rotation, pb.rotation, t);
                playbackSceneProp.transform.SetLocalPositionAndRotation(pos, rot);
            }
        }
        
        // ------ Events ------

        var events = a.Events;
        if (events == null || lastEventFrame == currentPlaybackFrame)
            return;

        lastEventFrame = currentPlaybackFrame;

        foreach (var evt in events)
        {
            switch (evt.type)
            {
                case EventType.OneShotFX:
                {
                    var fx = SpawnFX(evt);
                    
                    if (fx != null)
                    {
                        Structure source = null;
                        
                        if (evt.structureId != -1)
                        {
                            source = PlaybackStructures
                                .Select(s => s.GetComponent<Structure>())
                                .FirstOrDefault(s => s.structureID == evt.structureId);
                        }
                        
                        if (source != null)
                        {
                            fx.GetComponent<PooledVisualEffect>().parameterCollection.Apply(
                                fx.GetComponent<VisualEffect>(), 
                                source
                            );

                            fx.GetComponent<VisualEffect>().Play();
                        }
                    }

                    if (evt.fxType == FXOneShotType.GroundedSFX)
                        AudioManager.instance.Play(ReplayCache.SFX["Call_Modifier_Stomp"], evt.position);
                    
                    break;
                }
            }
        }

        ReplayAPI.OnPlaybackFrameInternal(a, b);

        foreach (var ext in ReplayAPI.Extensions)
        {
            if (!ext.IsEnabled)
                continue;
            
            ext.OnPlaybackFrame(a, b);
        }
    }

    private GameObject SpawnVFX(
        string poolName,
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        string tagType,
        Structure structure = null,
        bool deleteAfterSeconds = false
    )
    {
        var pool = PoolManager.instance.GetPool(poolName);

        if (pool == null)
            return null;

        var effect = Object.Instantiate(pool.poolItem.gameObject, parent);

        effect.transform.localPosition = localPosition;
        effect.transform.localRotation = localRotation;

        var visualEffect = effect.GetComponent<VisualEffect>();
        visualEffect.playRate = Abs(playbackSpeed);
        visualEffect.resetSeedOnPlay = false;
        visualEffect.startSeed = (uint)Random.Range(1, int.MaxValue);

        var pooledVisualEffect = effect.GetComponent<PooledVisualEffect>();
        if (pooledVisualEffect != null)
        {
            if (structure != null)
                pooledVisualEffect.UpdateParameterBasedOnStructure(structure);
        }
        
        var tag = effect.AddComponent<ReplayTag>();
        tag.Type = tagType;
        tag.attachedStructure = structure;

        if (deleteAfterSeconds)
            effect.AddComponent<DeleteAfterSeconds>();

        effect.AddComponent<FrameControlledVFX>().seed = visualEffect.startSeed;

        return effect;
    }
    
    public GameObject SpawnFX(EventChunk fx)
    {
        if (Recording.isRecording || Recording.isBuffering)
        {
            var evtChunk = new EventChunk
            {
                type = EventType.OneShotFX,
                fxType = fx.fxType,
                position = fx.position
            };

            if (fx.fxType == FXOneShotType.Ricochet)
                evtChunk.rotation = fx.rotation;

            if (fx.fxType == FXOneShotType.Hitmarker)
                evtChunk.damage = fx.damage;

            Recording.Events.Add(evtChunk);
        }

        GameObject vfxObject = null;

        bool isDust =
            fx.fxType is FXOneShotType.Break or
                FXOneShotType.DustImpact or
                FXOneShotType.Grounded or
                FXOneShotType.Spawn or
                FXOneShotType.Ungrounded;

        if (isDust && !Main.instance.ToggleDust.Value)
            return null;

        if (ReplayCache.FXToVFXName.TryGetValue(fx.fxType, out var poolName))
        {
            Structure source = null;

            if (fx.structureId != -1)
            {
                for (int i = 0; i < currentReplay.Header.Structures.Length; i++)
                {
                    if (currentReplay.Header.Structures[i].structureId != fx.structureId)
                        continue;

                    if (i < PlaybackStructures.Length && PlaybackStructures[i] != null)
                        source = PlaybackStructures[i].GetComponent<Structure>();

                    break;
                }
            }
            
            vfxObject = SpawnVFX(
                poolName,
                VFXParent.transform,
                fx.position,
                fx.rotation,
                poolName,
                source,
                true
            );
        }

        if (fx.fxType == FXOneShotType.Hitmarker && Main.instance.ToggleHitmarkers.Value)
        {
            var effect = SpawnVFX(
                "PlayerHitmarker",
                VFXParent.transform,
                fx.position,
                Quaternion.identity,
                "PlayerHitmarker",
                deleteAfterSeconds: true
            );

            var hitmarker = effect.GetComponent<PlayerHitmarker>();
            hitmarker.SetDamage(fx.damage);
            hitmarker.gameObject.SetActive(true);
            hitmarker.Play();
            hitmarker.GetComponent<VisualEffect>().playRate = Abs(playbackSpeed);
        }

        if (ReplayCache.FXToSFXName.TryGetValue(fx.fxType, out string audioName) 
            && ReplayCache.SFX.TryGetValue(audioName, out var audioCall))
            AudioManager.instance.Play(audioCall, fx.position);

        return vfxObject;
    } 
    
    // ----- Controls ------
    
    public void TogglePlayback(bool active, bool setSpeed = true, bool ignoreIsPlaying = true)
    {
        if (!isPlaying && !ignoreIsPlaying)
        {
            Main.ReplayError();
            return;
        }

        if (active && !isPaused) return;
        if (!active && isPaused) return;

        isPaused = !active;
        
        ReplayPlaybackControls.playButtonSprite.material
            .SetTexture("_Texture", !isPaused ? ReplayPlaybackControls.pauseSprite : ReplayPlaybackControls.playSprite);

        if (active)
        {
            AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], Main.instance.head.position);

            if (Main.instance.EnableHaptics.Value)
                Main.LocalPlayer.Controller.PlayerHaptics.PlayControllerHaptics(1f, 0.05f, 1f, 0.05f);

            if (setSpeed)
                SetPlaybackSpeed(previousPlaybackSpeed);
        }
        else
        {
            previousPlaybackSpeed = playbackSpeed;

            AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_ForwardUnlocked"], Main.instance.head.position);

            if (Main.instance.EnableHaptics.Value)
                Main.LocalPlayer.Controller.PlayerHaptics.PlayControllerHaptics(1f, 0.05f, 1f, 0.05f);

            if (setSpeed) 
                SetPlaybackSpeed(0f);
        }

        ReplayAPI.ReplayPauseChangedInternal(active);
    }
    
    public void SetPlaybackSpeed(float newSpeed)
    {
        playbackSpeed = newSpeed;

        if (isPlaying)
        {
            foreach (var structure in PlaybackStructures)
            {
                if (structure == null) continue;
            
                foreach (var vfx in structure.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = Abs(newSpeed);

                if (structure.GetComponent<Structure>().frictionVFX != null)
                    structure.GetComponent<Structure>().frictionVFX.returnToPoolTimer = null;
            }

            for (int i = 0; i < VFXParent.transform.childCount; i++)
            {
                var vfx = VFXParent.transform.GetChild(i);
                vfx.GetComponent<VisualEffect>().playRate = Abs(newSpeed);
            }

            foreach (var pedestal in Recording.Pedestals)
            {
                if (pedestal == null) continue;
            
                foreach (var vfx in pedestal.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = Abs(newSpeed);
            }
        }

        if (ReplayPlaybackControls.playbackSpeedText != null)
        {
            string label;

            if (Approximately(playbackSpeed, 0f))
                label = "Paused";
            else if (playbackSpeed < 0f)
                label = $"<< {Abs(playbackSpeed):0.0}x";
            else
                label = $">> {playbackSpeed:0.0}x";
            
            ReplayPlaybackControls.playbackSpeedText.text = label;
            ReplayPlaybackControls.playbackSpeedText.ForceMeshUpdate();
        }
    }

    public void AddPlaybackSpeed(float delta, float minSpeed = -8f, float maxSpeed = 8f)
    {
        TogglePlayback(isPaused && !Approximately(playbackSpeed + delta, 0), false);
        
        float speed = playbackSpeed + delta;

        if (Approximately(speed, 0))
            TogglePlayback(false);
        else
            TogglePlayback(true, false);
        
        speed = Round(speed * 10f) / 10f;
        speed = Clamp(speed, minSpeed, maxSpeed);
        SetPlaybackSpeed(speed);
    }

    public void SetPlaybackFrame(int frame)
    {
        int clampedFrame = Clamp(frame, 0, currentReplay.Frames.Length - 2);
        float time = currentReplay.Frames[clampedFrame].Time;
        SetPlaybackTime(time);
    }
    
    public void SetPlaybackTime(float time)
    {
        float oldTime = elapsedPlaybackTime;
        
        elapsedPlaybackTime = Clamp(time, 0f, currentReplay.Frames[^1].Time);

        for (int i = 0; i < currentReplay.Frames.Length - 2; i++)
        {
            if (currentReplay.Frames[i + 1].Time > elapsedPlaybackTime)
            {
                currentPlaybackFrame = i;
                break;
            }
        }

        Frame a = currentReplay.Frames[currentPlaybackFrame];
        Frame b = currentReplay.Frames[currentPlaybackFrame + 1];

        float span = b.Time - a.Time;
        float t = span > 0f
            ? (elapsedPlaybackTime - a.Time) / span
            : 1f;

        ApplyInterpolatedFrame(currentPlaybackFrame, Clamp01(t));
        UpdateVFX(oldTime);

        ReplayAPI.ReplayTimeChangedInternal(time);
    }

    private void UpdateVFX(float oldTime)
    {
        if (VFXParent == null)
            return;

        float delta = elapsedPlaybackTime - oldTime;

        foreach (var fx in VFXParent.GetComponentsInChildren<FrameControlledVFX>(true))
            ProcessFX(fx);

        foreach (var fxList in PlaybackStructures.Select(go => go.GetComponentsInChildren<FrameControlledVFX>()))
        {
            foreach (var fx in fxList)
                ProcessFX(fx);
        }

        void ProcessFX(FrameControlledVFX fx)
        {
            if (fx == null) return;

            bool paused = Abs(playbackSpeed) < 0.0001f;
            fx.vfx.pause = paused;
            fx.vfx.playRate = paused ? 1f : Abs(playbackSpeed);

            if (paused) // frame-stepping
            {
                if (delta > 0)
                    fx.StepForward(delta);
                else if (delta < 0)
                    fx.Evaluate(elapsedPlaybackTime);
            }
        }
    }
    
    public void UpdateReplayCameraPOV(Player player, bool hideLocalPlayer = false)
    {
        RecordingCamera cam = GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<RecordingCamera>(); 
        
        var localController = Main.LocalPlayer.Controller.transform;
        
        foreach (var renderer in localController.GetChild(1).GetComponentsInChildren<Renderer>())
            if (!renderer.name.Contains("Collider"))
                renderer.gameObject.layer = LayerMask.NameToLayer(hideLocalPlayer ? "ScreenFade" : "PlayerController");
        
        localController.GetChild(6).gameObject.SetActive(!hideLocalPlayer);
        
        if (povPlayer != null)
        {
            povPlayer.Controller?.PlayerVisuals?.renderer?.material?.SetInt("_IsLocalPlayer", 0);
            povPlayer.Controller.PlayerCamera.GetComponent<AudioListener>().enabled = false;
            povPlayer.Controller.transform.GetChild(6).gameObject.SetActive(true);
            povPlayer.Controller.PlayerHealth.transform.GetChild(0).gameObject.SetActive(false);
            povPlayer.Controller.ControllerType = ControllerType.Remote;
        }

        povPlayer = player;
        if (player != Main.LocalPlayer)
        {
            foreach (var mat in povPlayer.Controller?.PlayerVisuals?.renderer?.materials)
            {
                if (mat == null) continue;
                mat.SetInt("_IsLocalPlayer", 1);
            }
            
            povPlayer.Controller.transform.GetChild(6).gameObject.SetActive(false);
            povPlayer.Controller.transform.GetChild(9).gameObject.SetActive(false);
            povPlayer.Controller.PlayerHealth.transform.GetChild(0).gameObject.SetActive(true);
            povPlayer.Controller.PlayerHealth.transform.GetChild(1).gameObject.SetActive(false);
            
            povPlayer.Controller.ControllerType = ControllerType.Local;
            povPlayer.Controller.PlayerHealth.SetHealthBarPercentage(povPlayer.Data.HealthPoints, povPlayer.Data.HealthPoints, false);
            
            Main.LocalPlayer.Controller.PlayerCamera.GetComponent<AudioListener>().enabled = false;
            Main.LocalPlayer.Controller.PlayerNameTag.gameObject.SetActive(false);
            povPlayer.Controller.PlayerCamera.GetComponent<AudioListener>().enabled = false;
            
            foreach (var renderer in ReplayPlaybackControls.playbackControls.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.layer != LayerMask.NameToLayer("InteractionBase"))
                    renderer.gameObject.layer = LayerMask.NameToLayer("ScreenFade");
            }
            
            cam.localPlayerVR = povPlayer.Controller.PlayerVR;
        }
        else
        {
            cam.localPlayerVR = Main.LocalPlayer.Controller.PlayerVR;
            
            foreach (var mat in povPlayer.Controller?.PlayerVisuals?.renderer?.materials)
            {
                if (mat == null) continue;
                mat.SetInt("_IsLocalPlayer", 1);
            }
            
            povPlayer.Controller.ControllerType = ControllerType.Local;
            
            povPlayer.Controller.PlayerCamera.GetComponent<AudioListener>().enabled = false;
            Main.LocalPlayer.Controller.PlayerCamera.GetComponent<AudioListener>().enabled = true;
            
            foreach (var renderer in localController.GetChild(1).GetComponentsInChildren<Renderer>())
                renderer.gameObject.layer = LayerMask.NameToLayer("PlayerController");
                
            foreach (var renderer in ReplayPlaybackControls.playbackControls.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.layer != LayerMask.NameToLayer("InteractionBase"))
                    renderer.gameObject.layer = LayerMask.NameToLayer("Default");
            }
        }
    }
    
    // States
    public struct PlaybackStructureState
    {
        public bool active;
        public bool isLeftHeld;
        public bool isRightHeld;
        public bool isFlicked;
        public Structure.PhysicsState currentState;
    }

    public struct PlaybackPlayerState
    {
        public bool active;
        public int health;
        public short currentStack;
        public PlayerShiftstoneVFX activeShiftstoneVFX;
        public int leftShiftstone;
        public int rightShiftstone;
        public bool rockCamActive;
        public PlayerMeasurement playerMeasurement;
        public string visualData;
    }

    public struct PlaybackPedestalState
    {
        public bool active;
    }
    
    [RegisterTypeInIl2Cpp]
    public class Clone : MonoBehaviour
    {
        public GameObject VRRig;
        public GameObject LeftHand;
        public GameObject RightHand;
        public GameObject Head;
        public PlayerController Controller;

        public PlayerHandPresence.HandPresenceInput lHandInput;
        public PlayerHandPresence.HandPresenceInput rHandInput;

        private static readonly int PoseFistsActiveHash = Animator.StringToHash("PoseFistsActive");
        public float lastDashTime = -999f;
        private const float dashDuration = 1f;

        public PlayerAnimator pa;
        public PlayerMovement pm;
        public PlayerPoseSystem ps;

        public List<VoiceTrack> VoiceTracks = new();
        public AudioSource VoiceSource;
        
        public VoiceTrack currentTrack;

        public void ApplyInterpolatedPose(PlayerState a, PlayerState b, float t)
        {
            VRRig.transform.localPosition = Vector3.Lerp(a.VRRigPos, b.VRRigPos, t);
            VRRig.transform.localRotation =Quaternion.Slerp(a.VRRigRot, b.VRRigRot, t);
            
            Head.transform.localPosition = Vector3.Lerp(a.HeadPos, b.HeadPos, t);
            Head.transform.localRotation = Quaternion.Slerp(a.HeadRot, b.HeadRot, t);
            
            LeftHand.transform.localPosition = Vector3.Lerp(a.LHandPos, b.LHandPos, t);
            LeftHand.transform.localRotation = Quaternion.Slerp(a.LHandRot, b.LHandRot, t);
            
            RightHand.transform.localPosition = Vector3.Lerp(a.RHandPos, b.RHandPos, t);
            RightHand.transform.localRotation = Quaternion.Slerp(a.RHandRot, b.RHandRot, t);
        }

        bool IsDashing()
        {
            if (!pm.IsGrounded())
                lastDashTime = -999f;
            
            return Abs(Main.Playback.elapsedPlaybackTime - lastDashTime) < dashDuration;
        }

        public void Update()
        {
            if (Controller == null)
                return;

            if (pa == null || pm == null || ps == null)
            {
                pa = Controller.PlayerAnimator;
                pm = Controller.PlayerMovement;
                ps = Controller.PlayerPoseSystem;
            }
            
            int state;

            if (pm.IsGrounded())
                state = IsDashing() ? 4 : 1;
            else
                state = 2;

            pa.animator.SetInteger(pa.movementStateAnimatorHash, state);
            
            if (Main.instance.CloseHandsOnPose.Value)
                pa.animator.SetBool(PoseFistsActiveHash, ps.IsDoingAnyPose());

            Controller.PlayerEyeSystem.blinkSpeed = 0.1f / Abs(Main.Playback.playbackSpeed);
            
            UpdateVoicePlayback();
        }

        private void UpdateVoicePlayback()
        {
            if (VoiceSource == null)
            {
                VoiceSource = Controller.PlayerVoiceSystem.GetComponent<AudioSource>();
                VoiceSource.spatialBlend = 1f;
            }
            
            if (!Main.instance.ToggleVoices.Value)
            {
                if (VoiceSource != null && VoiceSource.isPlaying)
                    VoiceSource.Stop();

                currentTrack = null;
                return;
            }

            float replayTime = Main.Playback.elapsedPlaybackTime;

            VoiceTrack active = null;

            foreach (var t in VoiceTracks)
            {
                if (t.Clip == null) continue;

                float end = t.StartTime + (t.Clip.samples / (float)t.Clip.frequency);

                if (replayTime >= t.StartTime && replayTime < end)
                {
                    active = t;
                    break;
                }
            }

            if (active == null)
            {
                if (currentTrack != null || VoiceSource.isPlaying)
                    VoiceSource.Stop();

                currentTrack = null;
                VoiceSource.clip = null;
                return;
            }

            float localTime = replayTime - active.StartTime;
            localTime = Clamp(localTime, 0f, Max(0f, (active.Clip.samples / (float)active.Clip.frequency) - 0.01f));

            bool changedTrack = active != currentTrack || VoiceSource.clip != active.Clip;
            
            if (changedTrack)
            {
                VoiceSource.Stop();
                currentTrack = active;
                VoiceSource.clip = active.Clip;
            }

            VoiceSource.pitch = Main.Playback.playbackSpeed;
            VoiceSource.volume = Main.instance.VoiceVolume.Value;

            if (Abs(VoiceSource.time - localTime) > 0.1f || changedTrack)
                VoiceSource.time = localTime;

            if (Main.Playback.playbackSpeed == 0f)
            {
                VoiceSource.Pause();
                return;
            }

            if (!VoiceSource.isPlaying)
                VoiceSource.Play();
        }

        public class VoiceTrack
        {
            public float StartTime;
            public AudioClip Clip;
        }
    }
    
    [RegisterTypeInIl2Cpp]
    public class ReplayTag : MonoBehaviour
    {
        public string Type;
        public Structure attachedStructure;
    }

    [RegisterTypeInIl2Cpp]
    public class FrameControlledVFX : MonoBehaviour
    {
        public float spawnTime;
        public uint seed = 12345u;
        public VisualEffect vfx;

        public void Awake()
        {
            spawnTime = Main.Playback.elapsedPlaybackTime;
            vfx = GetComponent<VisualEffect>();
        }

        public void Evaluate(float time)
        {
            float age = time - spawnTime;

            if (age < 0)
                return;

            float fps = Main.Playback.currentReplay.Header.TargetFPS;

            float dt = 1f / fps;

            int fullFrames = FloorToInt(age / dt);
            float remainder = age - fullFrames * dt;

            vfx.pause = true;
            vfx.resetSeedOnPlay = false;
            vfx.startSeed = seed;

            vfx.Reinit();

            if (fullFrames > 0)
                vfx.Simulate(dt, (uint)fullFrames);

            if (remainder > 0.0001f)
                vfx.Simulate(remainder);

            vfx.pause = true;
        }

        public void StepForward(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;
            
            if (vfx == null) return;

            vfx.pause = true;
            vfx.Simulate(deltaTime);
            vfx.pause = true;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class FrictionController : MonoBehaviour
    {
        private Structure structure;
        private Vector3 velocity;

        private PooledVisualEffect currentFrictionVfx;
        private PooledAudioSource currentFrictionSource;

        private float vfxReturnTimer;

        public void Awake()
        {
            structure = GetComponent<Structure>();
        }

        public void Evaluate(float deltaTime, bool grounded, Vector3 structureVelocity)
        {
            velocity = structureVelocity;
            float speed = velocity.magnitude;

            bool sliding =
                grounded &&
                speed > structure.frictionFXTreshold;

            if (sliding)
            {
                vfxReturnTimer = structure.frictionVFXReturnalDelay;

                UpdateFrictionVFX();
                UpdateFrictionAudio();
            }
            else
            {
                vfxReturnTimer -= deltaTime;

                currentFrictionSource?.audioSource?.Stop();
                currentFrictionSource?.ReturnToPool();
                currentFrictionSource = null;
                
                currentFrictionVfx?.visualEffect?.Stop();

                if (vfxReturnTimer <= 0f && currentFrictionVfx != null)
                {
                    Destroy(currentFrictionVfx.GetComponent<FrameControlledVFX>());
                    currentFrictionVfx.ReturnToPool();
                    currentFrictionVfx = null;
                }
            }
        }

        public void UpdateFrictionVFX()
        {
            if (currentFrictionVfx == null)
            {
                currentFrictionVfx = PoolManager.instance.GetPooledObject("DustGroundedFriction_VFX").GetComponent<PooledVisualEffect>();

                currentFrictionVfx.transform.SetParent(transform, false);

                var bounds = structure.meshRenderer.bounds;
                currentFrictionVfx.transform.position = new Vector3(
                    bounds.center.x,
                    bounds.min.y,
                    bounds.center.z
                );
                
                currentFrictionVfx.transform.localRotation = Quaternion.identity;
                currentFrictionVfx.UpdateParameterBasedOnStructure(structure);

                currentFrictionVfx.visualEffect.resetSeedOnPlay = false;
                currentFrictionVfx.visualEffect.startSeed = (uint)Random.Range(0, int.MaxValue);
                
                currentFrictionVfx.gameObject.AddComponent<FrameControlledVFX>().seed = currentFrictionVfx.visualEffect.startSeed;
                currentFrictionVfx.ActivateEffect();
            }

            currentFrictionVfx.visualEffect.SetVector3(structure.frictionVFXVelocityParamater, velocity);
        }

        public void UpdateFrictionAudio()
        {
            if (currentFrictionSource == null)
            {
                currentFrictionSource = PoolManager.instance.GetPooledObject("Audio_SFX").GetComponent<PooledAudioSource>();

                currentFrictionSource.transform.SetParent(transform, false);
                currentFrictionSource.transform.localPosition = Vector3.zero;
                currentFrictionSource.transform.localRotation = Quaternion.identity;

                AudioManager.instance.Play(
                    structure.frictionAudioCall,
                    currentFrictionSource.audioSource,
                    true
                );
            }

            float volume = structure.frictionAudioVolumeCurve.Evaluate(velocity.magnitude);
            currentFrictionSource.audioSource.volume = volume * Main.Playback.playbackSpeed;
        }

        private void OnDisable()
        {
            currentFrictionSource?.audioSource?.Stop();
            currentFrictionSource?.ReturnToPool();
            currentFrictionSource = null;

            if (currentFrictionVfx != null)
            {
                Destroy(currentFrictionVfx.GetComponent<FrameControlledVFX>());

                currentFrictionVfx.visualEffect?.Stop();
                currentFrictionVfx.ReturnToPool();
                currentFrictionVfx = null;
            }

            velocity = Vector3.zero;
            vfxReturnTimer = 0f;
        }
    }
}