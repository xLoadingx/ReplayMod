using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Voice;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using MelonLoader;
using ReplayMod.Replay;
using ReplayMod.Replay.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using static UnityEngine.Mathf;
using EventType = ReplayMod.Replay.Serialization.EventType;
using SceneManager = Il2CppRUMBLE.Managers.SceneManager;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace ReplayMod.Core;

public class Patches
{
    public static List<(string playerId, short stackId)> activations = new();

    public static Dictionary<string, Queue<(float time, int damage)>> damageQueues = new();
    public static Dictionary<string, float> lastLargeDamageTime = new();
    public static HashSet<VisualEffect> frameSeenEffects = new();
    
    // ----- Pools -----
    [HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Instantiate))]
    public class Patch_PoolManager_Instantiate
    {
        static void Postfix(GameObject __result)
        {
            if (!Main.instance.UIInitialized)
                return;
            
            if (!Main.Recording.isRecording && !Main.Recording.isBuffering)
                return;
    
            var structure = __result.GetComponent<Structure>();
            Main.Recording.TryRegisterStructure(structure);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play))]
    [HarmonyPatch(new[] { typeof(AudioCall), typeof(Vector3), typeof(bool) })]
    public class Patch_AudioManager_Play
    {
        static void Postfix(Vector3 position, AudioCall call)
        {
            if (call == null || !Main.instance.UIInitialized)
                return;
    
            FXOneShotType? type = ReplayCache.AudioCallToFX.TryGetValue(call.name, out var foundType) ? foundType : null;
    
            if (type.HasValue)
            {
                Main.Recording.Events.Add(new EventChunk
                {
                    type = EventType.OneShotFX,
                    fxType = type.Value,
                    position = position
                });
            }
        }
    }

    [HarmonyPatch(typeof(Pool<PooledMonoBehaviour>), nameof(Pool<PooledMonoBehaviour>.FetchFromPool))]
    [HarmonyPatch(new Type[]
        { })]
    public class Patch_PoolMonoBehavior_FetchFromPoolNoParameters
    {
        static void Postfix(PooledMonoBehaviour __result)
        {
            var vfx = __result.GetComponent<VisualEffect>(); 
            if (vfx == null) 
                return;
    
            vfx.playRate = 1f;
        }
    }

    [HarmonyPatch(typeof(Pool<PooledMonoBehaviour>), nameof(Pool<PooledMonoBehaviour>.FetchFromPool))]
    [HarmonyPatch(new[] { typeof(Vector3), typeof(Quaternion) })]
    public class Patch_PoolMonoBehavior_FetchFromPool
    {
        static void Postfix(PooledMonoBehaviour __result, Vector3 position, Quaternion rotation)
        {
            if (Main.currentScene == "Loader" || !Main.instance.UIInitialized)
                return;
            
            var vfx = __result.GetComponent<VisualEffect>(); 
            if (vfx == null) 
                return; 
            
            string name = vfx.name;

            if (name == "Ricochet_VFX")
            {
                var evt = new EventChunk
                {
                    type = EventType.OneShotFX,
                    fxType = FXOneShotType.Ricochet,
                    position = vfx.transform.position,
                    rotation = vfx.transform.rotation
                };

                Main.Recording.Events.Add(evt);
            } else if (name == "Hitmarker")
            {
                var evt = new EventChunk
                {
                    type = EventType.OneShotFX,
                    fxType = FXOneShotType.Hitmarker,
                    position = vfx.transform.position,
                    damage = (byte)vfx.GetFloat("Damage")
                };

                Main.Recording.Events.Add(evt);
            }
    
            if (Main.Playback.playbackSpeed != 0f && Main.Playback.isPlaying)
            {
                float minDistance = 999999f;
                
                if (name is "VFX_Dust_Modifier_Jump" or "VFX_Dust_Modifier_Dash" && Main.Playback.PlaybackPlayers?.Length > 0)
                {
                    minDistance = Main.Playback.PlaybackPlayers
                        .Select(player => Vector3.Distance(player.Controller.transform.GetChild(1).GetChild(2).position, vfx.transform.position))
                        .Min();
                }
    
                if (name is "VFX_Dust_Modifier_Free" or "VFX_Dust_Modifier_Ground" && Main.Playback.PlaybackStructures?.Length > 0)
                {
                    minDistance = Main.Playback.PlaybackStructures
                        .Select(structure => Vector3.Distance(structure.transform.position, vfx.transform.position))
                        .Min();
                }
                
                if (minDistance < 1f * Main.Playback.ReplayRoot?.transform?.localScale.magnitude)
                {
                    vfx.playRate = Abs(Main.Playback.playbackSpeed); 
                    GameObject.Destroy(vfx.GetComponent<PooledVisualEffect>()); 
                    vfx.transform.SetParent(Main.Playback.VFXParent.transform);
                    vfx.transform.localScale = Vector3.Scale(vfx.transform.localScale, Main.Playback.ReplayRoot.transform.localScale);
                    vfx.gameObject.AddComponent<DeleteAfterSeconds>(); 
                    return;
                }
                
                vfx.playRate = 1f;
            }
        }
    }

    [HarmonyPatch(typeof(VFXFloatSwitch), nameof(VFXFloatSwitch.Apply))]
    public class Patch_VFXFloatSwitch_Apply
    {
        static void Prefix(VFXFloatSwitch __instance, VisualEffect effect, int structureID)
        {
            if (!Main.instance.UIInitialized)
                return;
            
            if (!Main.Recording.isRecording && !Main.Recording.isBuffering)
                return;
            
            if (!ReplayCache.VFXNameToFX.TryGetValue(effect.name, out var type))
                return;

            if (!frameSeenEffects.Add(effect))
                return;
            
            var evt = new EventChunk
            {
                type = EventType.OneShotFX,
                fxType = type,
                position = effect.transform.position,
                structureId = structureID
            };

            Main.Recording.Events.Add(evt);
        }
    }
    
    // ----- Events -----

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.ReduceHealth))]
    public class Patch_PlayerHealth_ReduceHealth
    {
        static void Postfix(PlayerHealth __instance, short amount)
        {
            if (!Main.instance.UIInitialized)
                return;
            
            if (!Main.Recording.isRecording && !Main.Recording.isBuffering)
                return;
            
            if (!Main.instance.EnableLargeDamageMarker.Value)
                return;
            
            string playerId = __instance.parentController.assignedPlayer.Data.GeneralData.PlayFabMasterId;

            if (!damageQueues.TryGetValue(playerId, out var queue))
            {
                queue = new Queue<(float, int)>();
                damageQueues[playerId] = queue;
            }

            float time = Main.Recording.lastSampleTime;
            queue.Enqueue((time, amount));

            while (queue.Count > 0 && queue.Peek().time < time - Main.instance.DamageWindow.Value)
                queue.Dequeue();

            int sum = 0;
            foreach (var e in queue)
                sum += e.damage;

            float oldestTimeInQueue = queue.Count > 0 ? queue.Peek().time : -999f;
            float lastProcessed = lastLargeDamageTime.GetValueOrDefault(playerId, -999f);

            if (sum >= Main.instance.DamageThreshold.Value && oldestTimeInQueue > lastProcessed)
            {
                lastLargeDamageTime[playerId] = time;
                queue.Clear();

                Main.Recording.AddMarker("core.largeDamage", Color.red);
            }
        }
    }
    
    // ----- Late-Player joining/leaving -----
    
    [HarmonyPatch(typeof(PlayerVisuals), nameof(PlayerVisuals.ApplyPlayerVisuals))]
    public class Patch_PlayerVisuals_ApplyPlayerVisuals
    {
        static void Postfix(PlayerVisuals __instance)
        {
            if (!Main.instance.UIInitialized)
                return;
            
            if (!Main.Recording.isRecording && !Main.Recording.isBuffering)
                return;

            var player = __instance.ParentController.assignedPlayer;
            if (player == null) return;

            Main.Recording.RegisterPlayer(player);
        }

        public static IEnumerator VisualDataDelay(Player player)
        {
            yield return new WaitForSeconds(0.1f);

            if (player == null)
                yield break;

            var id = player.Data.GeneralData.PlayFabMasterId;
            Main.Recording.PlayerInfos[id] = new PlayerInfo(player);
        }
    }

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.DeActivate))]
    public class Patch_PlayerController_Destroy
    {
        static void Prefix(PlayerController __instance)
        {
            if (!Main.instance.UIInitialized)
                return;
            
            if (!Main.Recording.isRecording && !Main.Recording.isBuffering)
                return;

            string id = __instance?.assignedPlayer?.Data?.GeneralData?.PlayFabMasterId;

            if (Main.Recording.PlayerSlots.TryGetValue(id, out var slot))
            {
                Main.Recording.RecordedPlayers[slot] = null;
            }
        }
    }

    // ----- Stack Recording -----
    [HarmonyPatch(typeof(PlayerStackProcessor), nameof(PlayerStackProcessor.Execute))]
    public class Patch_PlayerStackProcessor_Execute
    {
        static void Postfix(Stack stack, PlayerStackProcessor __instance)
        {
            if (!Main.instance.UIInitialized)
                return;
            
            if (!Main.Recording.isRecording && !Main.Recording.isBuffering)
                return;
    
            if (ReplayCache.NameToStackType.TryGetValue(stack.cachedName, out var type))
                activations.Add((__instance.ParentController.assignedPlayer.Data.GeneralData.PlayFabMasterId, (short)type));
        }
    }
    
    // ----- Other -----
    [HarmonyPatch(typeof(SceneManager), nameof(SceneManager.LoadSceneAsync))]
    [HarmonyPatch(new[] { typeof(int), typeof(bool), typeof(bool), typeof(float), typeof(LoadSceneMode), typeof(AudioCall) })]
    public class Patch_SceneManager_LoadSceneAsync
    {
        static void Prefix()
        {
            if (!Main.instance.UIInitialized)
                return;
            
            Main.Playback.StopReplay();
            
            if (Main.Recording.isRecording) Main.Recording.StopRecording();

            var shiftstones = Main.LocalPlayer.Controller.PlayerShiftstones.GetEquippedShiftstones();
            Main.instance.leftShiftstonePool = shiftstones[0]?.resourceName;
            Main.instance.rightShiftstonePool = shiftstones[1]?.resourceName;
        }
    }

    [HarmonyPatch(typeof(ParkBoardTrigger), nameof(ParkBoardTrigger.OnTriggerEnter))]
    public class Patch_ParkBoardTrigger_OnTriggerEnter
    {
        static void Postfix(Collider other)
        {
            // In a replay park
            if (PhotonNetwork.CurrentRoom == null && Main.currentScene == "Park")
                MelonCoroutines.Start(Utilities.LoadMap(1));
        }
    }

    [HarmonyPatch(typeof(PlayerHandPresence), nameof(PlayerHandPresence.UpdateHandPresenceAnimationStates))]
    public class Patch_PlayerHandPresence_UpdateHandPresenceAnimationStates
    {
        static void Prefix(PlayerHandPresence __instance, InputManager.Hand hand, ref PlayerHandPresence.HandPresenceInput input)
        {
            if (!Main.instance.UIInitialized)
                return;
            
            if (__instance.parentController == null) return;
            
            if (!Main.Playback.isPlaying || !Utilities.IsReplayClone(__instance.parentController) || Main.Playback.PlaybackPlayers == null)
                return;

            ReplayPlayback.Clone playbackPlayer = null;
            foreach (var player in Main.Playback.PlaybackPlayers)
            {
                if (player == null)
                    continue;

                if (player.Controller == __instance.parentController)
                {
                    playbackPlayer = player;
                    break;
                }
            }

            if (playbackPlayer == null)
                return;

            input = hand == InputManager.Hand.Left
                ? playbackPlayer.lHandInput
                : playbackPlayer.rHandInput;
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.AttemptResetHealth))]
    public class Patch_PlayerHealth_AttemptResetHealth 
    {
        static bool Prefix(PlayerHealth __instance)
        {
            if (!Main.instance.UIInitialized)
                return true;
            
            if (!Main.Playback.isPlaying || !Utilities.IsReplayClone(__instance.parentController))
                return true;

            return false;
        }
    }
    [HarmonyPatch(typeof(VoiceClient), nameof(VoiceClient.createLocalVoice))]
    public static class Patch_VoiceClient_createLocalVoice
    {
        static void Postfix(Il2CppPhoton.Voice.VoiceClient __instance, Il2CppPhoton.Voice.LocalVoice __result, int __0, Il2CppSystem.Func<byte, int, Il2CppPhoton.Voice.LocalVoice> __1)
        {
            if (Main.Recording.isRecording){
                __result.DebugEchoMode = true;
            }
        }
    }
  }

