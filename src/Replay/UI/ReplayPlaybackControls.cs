using System;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.Mathf;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.UI;

public static class ReplayPlaybackControls
{
    public static bool playbackControlsOpen;

    public static GameObject playbackControls;
    public static GameObject timeline;
    public static TextMeshPro totalDuration;
    public static TextMeshPro currentDuration;
    public static TextMeshPro playbackTitle;
    public static TextMeshPro playbackSpeedText;

    public static MeshRenderer playButtonSprite;
    public static Texture2D pauseSprite;
    public static Texture2D playSprite;

    public static DestroyOnPunch destroyOnPunch;

    public static GameObject markerPrefab;
    
    public static float smoothing = 7f;

    public static void Update()
    {
        var head = Main.instance.head;
        if (head == null || playbackControls == null || !(bool)Main.instance.PlaybackControlsFollow.SavedValue)
            return;

        float armSpan = Main.LocalPlayer.Data.PlayerMeasurement.ArmSpan;
        float distanceToPlayer = Vector3.Distance(playbackControls.transform.position, head.position);

        if (distanceToPlayer < armSpan)
            return;

        var (targetPos, targetRot) = GetTargetSlabTransform(head);

        float proximityT = InverseLerp(armSpan, armSpan * 1.5f, distanceToPlayer);
        float scaledT = (1f - Exp(-smoothing * Time.deltaTime)) * proximityT;

        playbackControls.transform.position = Vector3.Lerp(playbackControls.transform.position, targetPos, scaledT);
        playbackControls.transform.rotation = Quaternion.Slerp(playbackControls.transform.rotation, targetRot, scaledT);
    }

    public static (Vector3 position, Quaternion rotation) GetTargetSlabTransform(Transform head)
    {
        Vector3 forward = head.forward;
        forward.y = 0f;
        
        Vector3 position = head.position + forward * 0.6f + Vector3.down * 0.1f;

        Vector3 lookDir = head.position - position;
        lookDir.y = 0f;

        Quaternion rotation = Quaternion.LookRotation(lookDir);

        return (position, rotation);
    }
    
    public static void Open()
    {
        if (Main.instance.head == null) return;

        if (playbackControlsOpen)
            playbackControls.SetActive(false);
        
        playbackControlsOpen = true;

        var (position, rotation) = GetTargetSlabTransform(Main.instance.head);
        
        if (Main.LocalPlayer.Controller.GetSubsystem<PlayerMovement>().IsGrounded())
        {
            playbackControls.transform.position = position;
            playbackControls.transform.rotation = rotation;
            playbackControls.SetActive(true);
            playbackControls.transform.GetChild(0).gameObject.SetActive(true);

            AudioManager.instance.Play(ReplayCache.SFX["Call_Slab_Construct"], Main.instance.head.position);
        }
    }

    public static void Close()
    {
        if (!playbackControlsOpen) return;
        
        playbackControlsOpen = false;
    
        playbackControls.SetActive(false);
        playbackControls.transform.GetChild(0).gameObject.SetActive(false);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_Slab_Dismiss"], Main.instance.head.position);
        PoolManager.instance.GetPool("DustBreak_VFX").FetchFromPool(playbackControls.transform.position, playbackControls.transform.rotation)
            .transform.localScale = Vector3.one * 0.4f;
    }
}

[RegisterTypeInIl2Cpp]
public class DestroyOnPunch : MonoBehaviour
{
    public InteractionHand leftHand;
    public InteractionHand rightHand;
    public Action onDestroy;

    public float punchThreshold = 3.0f;

    public void OnTriggerEnter(Collider other)
    {
        Vector3 velocity = Vector3.zero;

        bool isLeftHand = true;
        if (other.name.Contains("Bone_Pointer_C_L"))
        {
            velocity = leftHand.GetComponent<Rigidbody>().velocity;
        }
        else if (other.name.Contains("Bone_Pointer_C_R"))
        {
            velocity = rightHand.GetComponent<Rigidbody>().velocity;
            isLeftHand = false;
        }

        if (velocity.magnitude > punchThreshold)
        {
            onDestroy?.Invoke();
            
            if ((bool)Main.instance.EnableHaptics.SavedValue)
                Main.LocalPlayer.Controller.GetSubsystem<PlayerHaptics>().PlayControllerHaptics(
                    isLeftHand ? 1f : 0f, isLeftHand ? 0.2f : 0f, !isLeftHand ? 1f : 0f, !isLeftHand ? 0.2f : 0f
                );
        }
    }
}