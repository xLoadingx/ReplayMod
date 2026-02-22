using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Environment.MatchFlow;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppTMPro;
using MelonLoader;
using ReplayMod.Replay;
using ReplayMod.Replay.Files;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using UnityEngine;
using UnityEngine.VFX;
using Player = Il2CppRUMBLE.Players.Player;

namespace ReplayMod.Core;

public class ReplayRecording
{
    public bool isRecording = false;
    
    public  float lastSampleTime = 0f;

    public int pingSum;
    public int pingCount;
    public int pingMin = int.MaxValue;
    public int pingMax = 0;
    public float pingTimer = 0f;
    
    public string recordingSceneName;

    public static TextMeshPro recordingIcon;
    
    public List<Frame> Frames = new();
    public List<EventChunk> Events = new();
    public List<Marker> recordingMarkers = new();
    
    public List<Player> RecordedPlayers = new();
    public Dictionary<string, int> PlayerSlots = new();
    public Dictionary<string, PlayerInfo> PlayerInfos = new();
    public List<StructureInfo> StructureInfos = new();
    
    // Replay Buffer
    public Queue<Frame> replayBuffer = new();
    public Queue<Marker> bufferMarkers = new();
    public bool isBuffering = false;
    
    // World
    public List<Structure> Structures = new();
    public List<GameObject> Pedestals = new();
    
    public void HandleRecording()
    {
        if ((!isRecording && !isBuffering) || SceneManager.instance.IsLoadingScene) return;

        float sampleRate = 1f / (int)Main.instance.TargetRecordingFPS.SavedValue;

        if (Time.time - lastSampleTime >= sampleRate)
        {
            lastSampleTime = Time.time;

            var frame = CaptureFrame();
            frame.Time = Time.time;

            if (isRecording)
            {
                var cloned = frame.Clone();
                ReplayAPI.OnRecordFrameInternal(cloned, false);
                Frames.Add(cloned);
            }
            
            if (isBuffering)
            {
                var cloned = frame.Clone();
                ReplayAPI.OnRecordFrameInternal(cloned, true);
                replayBuffer.Enqueue(cloned);

                float cutoffTime = frame.Time - (int)Main.instance.ReplayBufferDuration.SavedValue;
                while (replayBuffer.Count > 0 && replayBuffer.Peek().Time < cutoffTime)
                    replayBuffer.Dequeue();
            }

            Patches.activations.Clear();
            Events.Clear();
        }

        if (Time.time - pingTimer >= 1f)
        {
            pingTimer = Time.time;

            bool updated = false;

            if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer?.CustomProperties?.TryGetValue("ping", out var objPing) == true)
            {
                int ping = objPing.Unbox<int>();

                if (!PhotonNetwork.IsMasterClient && PhotonNetwork.MasterClient?.CustomProperties?.TryGetValue("ping", out var objHostPing) == true)
                    ping += objHostPing.Unbox<int>();

                if (ping >= 0)
                {
                    pingSum += ping;
                    pingCount++;

                    if (ping < pingMin) pingMin = ping;
                    if (ping > pingMax) pingMax = ping;

                    updated = true;
                }
            }

            if (!updated && PhotonNetwork.IsConnected)
            {
                int fallbackPing = PhotonNetwork.GetPing();

                pingSum += fallbackPing;
                pingCount++;

                if (fallbackPing < pingMin) pingMin = fallbackPing;
                if (fallbackPing > pingMax) pingMax = fallbackPing;
            }
        }
    }
    
    Frame CaptureFrame()
    {
        var flickedStructures = new HashSet<GameObject>();

        // Players
        var playerStates = Utilities.NewArray<PlayerState>(RecordedPlayers.Count);

        for (int i = 0; i < RecordedPlayers.Count; i++)
        {
            var p = RecordedPlayers[i];
            
            if (p == null)
            {
                playerStates[i] = new PlayerState { active = false };
                continue;
            }

            var stackProc = p.Controller?.GetSubsystem<PlayerStackProcessor>();

            if (stackProc == null)
                continue;

            Stack flickStack = null;

            foreach (var stack in stackProc.availableStacks)
            {
                if (stack == null)
                    continue;

                if (stack.cachedName == "Flick")
                    flickStack = stack;

                if (flickStack?.runningExecutions != null)
                {
                    foreach (var exec in flickStack.runningExecutions)
                    {
                        if (exec?.TargetProcessable == null)
                            continue;
                        
                        var proc = exec.TargetProcessable.TryCast<ProcessableComponent>();
                        
                        if (proc?.gameObject != null)
                            flickedStructures.Add(proc.gameObject);
                    }
                }
            }
            
            Transform VRRig = p.Controller.transform.GetChild(2);
            Transform LHand = VRRig.GetChild(1);
            Transform RHand = VRRig.GetChild(2);
            Transform Head = VRRig.GetChild(0).GetChild(0);

            var shiftstoneSystem = p.Controller.GetSubsystem<PlayerShiftstoneSystem>();
            var currentShiftstoneEffects = shiftstoneSystem.currentShiftstoneEffects.ToArray();
            var shiftstones = shiftstoneSystem.GetCurrentShiftStoneConfiguration();
            int left = shiftstones is { Count: > 0 } ? shiftstones[0] : -1;
            int right = shiftstones is { Count: > 0 } ? shiftstones[1] : -1;
            
            PlayerShiftstoneVFX active = PlayerShiftstoneVFX.None;

            var charge = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "ChargeStone_VFX");
            if (charge != null && charge.isPlaying)
                active |= PlayerShiftstoneVFX.Charge;

            var adamant = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "AdamantStone_VFX");
            if (adamant != null && adamant.isPlaying)
                active |= PlayerShiftstoneVFX.Adamant;

            var surge = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "SurgeStone_VFX");
            if (surge != null && surge.isPlaying)
                active |= PlayerShiftstoneVFX.Surge;

            var vigor = currentShiftstoneEffects.FirstOrDefault(e => e.ResourceName == "Vigorstone_VFX");
            if (vigor != null && vigor.isPlaying)
                active |= PlayerShiftstoneVFX.Vigor;

            bool recordFingers = (bool)Main.instance.HandFingerRecording.SavedValue;
            var playerHandPresence = p.Controller.GetSubsystem<PlayerHandPresence>();
            var LhandInput = playerHandPresence.GetHandPresenceInputForHand(InputManager.Hand.Left);
            var RhandInput = playerHandPresence.GetHandPresenceInputForHand(InputManager.Hand.Right);

            var rockCam = p.Controller.GetSubsystem<PlayerLIV>()?.LckTablet.transform.gameObject;

            var measurement = p.Controller.assignedPlayer.Data.PlayerMeasurement;
            
            var playerState = new PlayerState
            {
                VRRigPos = VRRig.position,
                VRRigRot = VRRig.rotation,
                HeadPos = Head.localPosition,
                HeadRot = Head.localRotation,
                LHandPos = LHand.localPosition,
                LHandRot = LHand.localRotation,
                RHandPos = RHand.localPosition,
                RHandRot = RHand.localRotation,
                Health = p.Data.HealthPoints,
                currentStack = Patches.activations.FirstOrDefault(s => s.playerId == p.Data.GeneralData.PlayFabMasterId).stackId,
                active = p.Controller.gameObject.activeInHierarchy,
                activeShiftstoneVFX = active,
                leftShiftstone = left,
                rightShiftstone = right,
                ArmSpan = measurement.ArmSpan,
                Length = measurement.Length,
                visualData = p.Data.VisualData.ToPlayfabDataString()
            };

            if (recordFingers)
            {
                playerState.lgripInput = LhandInput.gripInput;
                playerState.lindexInput = LhandInput.indexInput;
                playerState.lthumbInput = LhandInput.thumbInput;
                playerState.rindexInput = RhandInput.indexInput;
                playerState.rthumbInput = RhandInput.indexInput;
                playerState.rgripInput = RhandInput.gripInput;
            }

            if (rockCam != null)
            {
                playerState.rockCamActive = rockCam.transform.GetChild(0).gameObject.activeInHierarchy;
                playerState.rockCamPos = rockCam.transform.position;
                playerState.rockCamRot = rockCam.transform.rotation;
            }

            playerStates[i] = playerState;
        }

        // Structures
        var structureStates = Utilities.NewArray<StructureState>(Structures.Count);
        
        for (int i = 0; i < Structures.Count; i++)
        {
            var structure = Structures[i];

            if (structure == null ||
                !structure.TryGetComponent(out Structure _) ||
                structure.name.StartsWith("Static Target") ||
                structure.name.StartsWith("Moving Target"))
                continue;
            
            bool hasReplayFlickVFX = structure.GetComponentsInChildren<ReplayPlayback.ReplayTag>().Any(t => t.Type == "StructureFlick");
            bool isTargetDisk = (structure.name.Contains("Disc") && structure.transform.GetChild(0).GetChild(0).gameObject.activeInHierarchy);

            var holdVFXCount = structure.GetComponentsInChildren<VisualEffect>().Count(vfx => vfx.name.Contains("Hold_VFX"));
            
            StructureStateType type = structure.processableComponent.currentState.name switch
            {
                "default" => StructureStateType.Default,
                "Frozen" => StructureStateType.Frozen,
                "Float" => StructureStateType.Float,
                "FreeGrounded" => StructureStateType.FreeGrounded,
                "StableGrounded" => StructureStateType.StableGrounded,
                "Normal" => StructureStateType.Normal,
                "Free" => StructureStateType.Free,
                _ => StructureStateType.Default
            };

            structureStates[i] = new StructureState
            {
                position = structure.transform.position,
                rotation = structure.transform.rotation,
                active = structure.gameObject.activeInHierarchy,
                grounded = structure.IsGrounded || (structure.activeJointControl != null && structure.processableComponent.currentState == structure.frozenState),
                isLeftHeld = holdVFXCount >= 1,
                isRightHeld = holdVFXCount >= 2,
                isFlicked = flickedStructures.Contains(structure.gameObject) || hasReplayFlickVFX,
                currentState = type,
                isTargetDisk = isTargetDisk
            };
        }

        // Pedestals
        var pedestalStates = Utilities.NewArray<PedestalState>(Pedestals.Count);

        for (int i = 0; i < Pedestals.Count; i++)
        {
            var pedestal = Pedestals[i];
            if (pedestal == null) continue;
            
            pedestalStates[i] = new PedestalState
            {
                position = pedestal.transform.position,
                active = pedestal.GetComponent<Pedestal>().enabled && pedestal.activeInHierarchy
            };
        }
        
        return new Frame
        {
            Structures = structureStates,
            Players = playerStates,
            Pedestals = pedestalStates,
            Events = Events.ToArray()
        };
    }
    
    public void SaveReplay(Frame[] frames, List<Marker> markers, string logPrefix, bool isBufferClip = false, Action<ReplayInfo, string> onSave = null)
    {
        if (frames.Length == 0)
        {
            Main.instance.LoggerInstance.Warning($"{logPrefix} stopped, but no frames were captured. Replay was not saved.");
            return;
        }
        
        float duration = frames[^1].Time - frames[0].Time;

        string customMap = Utilities.GetActiveCustomMapName();

        if (string.IsNullOrEmpty(customMap))
        {
            string[] rebuilt = Utilities.RebuildCustomMapFromScene();
            
            if (rebuilt != null)
                customMap = string.Join("|", rebuilt);
        }

        if (Main.currentScene == "Gym" && Main.instance.flatLandRoot?.gameObject.activeSelf == false)
            customMap = "FlatLandSingle";

        float startTime = frames[0].Time;

        foreach (var f in frames)
            f.Time -= startTime;

        foreach (var m in markers)
            m.time -= startTime;

        var replayInfo = new ReplayInfo
        {
            Header = new ReplaySerializer.ReplayHeader
            {
                Version = BuildInfo.Version,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Scene = recordingSceneName,
                Duration = duration,
                CustomMap = customMap,
                FrameCount = frames.Length,
                PedestalCount = Pedestals.Count,
                MarkerCount = markers.Count,
                AvgPing = pingCount > 0 ? pingSum / pingCount : -1,
                MinPing = pingMin,
                MaxPing = pingMax,
                TargetFPS = (int)Main.instance.TargetRecordingFPS.SavedValue,
                Structures = StructureInfos.ToArray(),
                Guid = Guid.NewGuid().ToString()
            },
            Frames = frames
        };

        var orderedInfos = new List<PlayerInfo>();

        foreach (var player in RecordedPlayers)
        {
            if (player == null)
            {
                orderedInfos.Add(null);
                continue;
            }
            
            var id = player.Data.GeneralData.PlayFabMasterId;
            orderedInfos.Add(PlayerInfos.GetValueOrDefault(id));
        }

        replayInfo.Header.Players = orderedInfos.ToArray();
        
        replayInfo.Header.MarkerCount = markers.Count;
        replayInfo.Header.Markers = markers.ToArray();
        
        string pattern = Utilities.GetFriendlySceneName(recordingSceneName) switch
        {
            "Gym" => ReplayFiles.LoadFormatFile("AutoNameFormats/gym"),
            "Park" => ReplayFiles.LoadFormatFile("AutoNameFormats/park"),
            "Pit" or "Ring" => ReplayFiles.LoadFormatFile("AutoNameFormats/match"),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(pattern))
        {
            Main.ReplayError($"{logPrefix} was not saved due to missing name format file.");
            pattern = "{Host} vs {Client} - {Scene}";
        }

        replayInfo.Header.Title = ReplayFormatting.FormatReplayString(pattern, replayInfo.Header);
        
        Main.instance.LoggerInstance.Msg($"{logPrefix} saved after {duration:F2}s ({frames.Length} frames)");

        string path = $"{ReplayFiles.replayFolder}/Replays/{ReplayFormatting.GetReplayName(replayInfo, isBufferClip)}";
        ReplayArchive.BuildReplayPackage(
            path,
            replayInfo,
            () =>
            {
                AudioManager.instance.Play(ReplayCache.SFX["Call_PoseGhost_PosePerformed"], 
                    Main.LocalPlayer.Controller.GetSubsystem<PlayerVR>().transform.position);
                Main.instance.LoggerInstance.Msg($"{logPrefix} saved to disk: '{path}'");
                
                if ((bool)Main.instance.EnableHaptics.SavedValue)
                    Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.15f, 1f, 0.15f);
                
                if (recordingIcon != null)
                {
                    recordingIcon.color = Color.green;
            
                    MelonCoroutines.Start(Utilities.LerpValue(
                        () => recordingIcon.color,
                        c => recordingIcon.color = c,
                        Color.Lerp,
                        isBufferClip && isRecording ? Color.red : new Color (0, 1, 0, 0),
                        0.7f,
                        Utilities.EaseOut,
                        delay: 0.1f
                    ));
                }

                onSave?.Invoke(replayInfo, path);

                ReplayFiles.ReloadReplays();
            }
        );
    }

    public bool TryRegisterStructure(Structure structure)
    {
        if (!isRecording && !isBuffering) 
            return false; 
        
        if (structure == null) 
            return false; 
        
        if (Structures.Contains(structure)) 
            return false; 
        
        if (structure.name.StartsWith("Static Target") || structure.name.StartsWith("Moving Target")) 
            return false; 
        
        Structures.Add(structure); 
        
        var name = structure.resourceName; 
        StructureType type = name switch
        {
            "RockCube" => StructureType.Cube, 
            "Pillar" => StructureType.Pillar, 
            "Disc" => StructureType.Disc, 
            "Wall" => StructureType.Wall, 
            "Ball" => StructureType.Ball, 
            "LargeRock" => StructureType.LargeRock, 
            "SmallRock" => StructureType.SmallRock, 
            "BoulderBall" => StructureType.CagedBall, 
            _ => StructureType.Cube
        };

        if (type == StructureType.Ball && structure.transform.childCount >= 3 && structure.transform.GetChild(2).name == "Ballcage")
        {
            type = structure.GetComponent<Tetherball>() != null ? StructureType.TetheredCagedBall : StructureType.CagedBall;
        } 
        
        StructureInfos.Add(new StructureInfo { Type = type }); return true;
    }
    
    public void SetupRecordingData()
    {
        RecordedPlayers.Clear();
        PlayerSlots.Clear();
        PlayerInfos.Clear();
        Structures.Clear();
        StructureInfos.Clear();
        Pedestals.Clear();

        recordingSceneName = Main.currentScene;
        
        foreach (var structure in CombatManager.instance.structures)
            TryRegisterStructure(structure);
        
        foreach (var player in PlayerManager.instance.AllPlayers)
        {
            if (player == null) continue;

            RecordedPlayers.Add(player);

            MelonCoroutines.Start(Patches.Patch_PlayerVisuals_ApplyPlayerVisuals.VisualDataDelay(player));
        }

        Pedestals.AddRange(Utilities.EnumerateMatchPedestals());
    }

    public int RegisterPlayer(Player player)
    {
        var id = player.Data.GeneralData.PlayFabMasterId;

        int index;
        if (PlayerSlots.TryGetValue(id, out var slot))
        {
            RecordedPlayers[slot] = player;
            index = slot;
        }
        else
        {
            int newSlot = RecordedPlayers.Count;
            PlayerSlots[id] = newSlot;
            RecordedPlayers.Add(player);

            index = newSlot;
        }
        
        MelonCoroutines.Start(Patches.Patch_PlayerVisuals_ApplyPlayerVisuals.VisualDataDelay(player));

        return index;
    }

    public Marker AddMarker(string name, Color color)
    {
        return AddMarker(name, color, Time.time);
    }

    public Marker AddMarker(string name, Color color, float time)
    {
        if (!isRecording && !isBuffering)
            return null;

        var marker = new Marker(name, time, color);

        if (isRecording)
            recordingMarkers.Add(marker);

        if (isBuffering)
            bufferMarkers.Enqueue(marker);

        return marker;
    }

    public void StartBuffering()
    {
        SetupRecordingData();
        replayBuffer.Clear();
        bufferMarkers.Clear();
        isBuffering = true;
    }
    
    public void SaveReplayBuffer()
    {
        var frames = replayBuffer.ToArray();
        SaveReplay(frames, bufferMarkers.ToList(), "Replay Buffer", true, (info, path) => ReplayAPI.ReplaySavedInternal(info, true, path));
    }
    
    public void StopRecording()
    {
        isRecording = false;
        SaveReplay(Frames.ToArray(), recordingMarkers, "Recording", onSave: (info, path) => ReplayAPI.ReplaySavedInternal(info, false, path));

        Reset();
    }
    
    public void StartRecording()
    {
        SetupRecordingData();
        Frames.Clear();
        Events.Clear();
        recordingMarkers.Clear();
        isRecording = true;

        if (recordingIcon != null)
            recordingIcon.color = Color.red;
        
        if ((bool)Main.instance.EnableHaptics.SavedValue)
            Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(1f, 0.15f, 1f, 0.15f);
    }

    public void Reset()
    {
        replayBuffer.Clear();
        lastSampleTime = 0f;

        pingCount = 0;
        pingSum = 0;
        pingMax = 0;
        pingMin = int.MaxValue;
    }
}