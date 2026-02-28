using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Il2CppRUMBLE.Managers;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using ReplayMod.Replay.Files;
using ReplayMod.Replay.Serialization;
using RumbleModdingAPI;
using RumbleModdingAPI.RMAPI;
using UnityEngine;
using UnityEngine.VFX;
using Random = UnityEngine.Random;
using static UnityEngine.Mathf;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.UI;

public static class ReplayCrystals
{
    public static GameObject crystalPrefab;
    public static GameObject crystalParent;
    public static List<Crystal> Crystals = new();
    public static Crystal heldCrystal;
    public static bool isHeldByRight;
    
    public static VisualEffect crystalizeVFX;

    public static (string path, Color32 color) lastReplayColor = new();
    
    public static void HandleCrystals()
    {
        const float grabRadius = 0.1f;

        if (heldCrystal != null)
        {
            bool released = 
                (isHeldByRight && Calls.ControllerMap.RightController.GetGrip() < 0.5f) ||
                (!isHeldByRight && Calls.ControllerMap.LeftController.GetGrip() < 0.5f);

            if (released)
            {
                heldCrystal.Release();
                heldCrystal.transform.SetParent(crystalParent.transform, true);
                
                heldCrystal = null;
                return;
            }
        }
        
        if (heldCrystal == null)
        {
            if (Calls.ControllerMap.RightController.GetGrip() > 0.5f)
            {
                var crystal = FindClosestCrystal(Main.instance.rightHand.position, grabRadius);
                if (crystal != null)
                {
                    heldCrystal = crystal;
                    isHeldByRight = true;
                    crystal.Grab();
                    crystal.transform.SetParent(Main.instance.rightHand.transform, true);
                }
            } else if (Calls.ControllerMap.LeftController.GetGrip() > 0.5f)
            {
                var crystal = FindClosestCrystal(Main.instance.leftHand.position, grabRadius);
                if (crystal != null)
                {
                    heldCrystal = crystal;
                    isHeldByRight = false;
                    crystal.Grab();
                    crystal.transform.SetParent(Main.instance.leftHand.transform, true);
                }
            }
        }
    }
    
    public static void LoadCrystals(string scene = null)
    {
        if (string.IsNullOrEmpty(scene))
            scene = Main.currentScene;
        
        string path = Path.Combine(
            MelonEnvironment.UserDataDirectory,
            "ReplayMod",
            "Settings",
            "replayCrystals.json"
        );

        if (!File.Exists(path))
            return;

        string json = File.ReadAllText(path);
        var allStates = JsonConvert.DeserializeObject<Dictionary<string, CrystalState[]>>(json);

        if (allStates != null && allStates.TryGetValue(scene, out var states))
        {
            Crystals = new();

            foreach (var state in states)
                CreateCrystal().RestoreState(state);
        }
    }

    public static void SaveCrystals(string scene = null)
    {
        if (string.IsNullOrEmpty(scene))
            scene = Main.currentScene;
        
        string path = Path.Combine(
            MelonEnvironment.UserDataDirectory,
            "ReplayMod",
            "Settings",
            "replayCrystals.json"
        );

        Dictionary<string, CrystalState[]> allStates = new();

        if (File.Exists(path))
        {
            string existingJson = File.ReadAllText(path);
            allStates = JsonConvert.DeserializeObject<Dictionary<string, CrystalState[]>>(existingJson)
                ?? new();
        }

        if (Crystals == null)
            return;

        var states = new List<CrystalState>();
        foreach (var crystal in Crystals)
        {
            if (crystal != null)
                states.Add(crystal.CaptureState());
        }

        allStates[scene] = states.ToArray();
        
        string newJson = JsonConvert.SerializeObject(allStates, Formatting.Indented);
        File.WriteAllText(path, newJson);
    }

    public static Crystal FindClosestCrystal(Vector3 handPos, float maxDistance)
    {
        Crystal closest = null;
        float closestSqr = maxDistance * maxDistance;

        foreach (var crystal in Crystals)
        {
            if (crystal == null)
                continue;
            
            float sqr = (crystal.transform.position - handPos).sqrMagnitude;
            if (sqr < closestSqr)
            {
                closestSqr = sqr;
                closest = crystal;
            }
        }

        return closest;
    }
    
    public static Crystal CreateCrystal(Vector3 position, ReplaySerializer.ReplayHeader header, string path, bool useAnimation = false, bool applyRandomColor = false)
    {
        if (crystalParent == null)
            crystalParent = new GameObject("Crystals");
        
        Crystal crystal = GameObject.Instantiate(crystalPrefab, crystalParent.transform).AddComponent<Crystal>();
            
        var name = Path.GetFileNameWithoutExtension(path).StartsWith("Replay") ? header.Title : Path.GetFileNameWithoutExtension(path);
        
        crystal.name = $"Crystal ({name}, {header.Date})";
        crystal.transform.position = position;
        crystal.Title = name;

        GameObject text = Create.NewText(name, 1f, Color.white, Vector3.zero, Quaternion.identity);

        text.name = "Replay Title";
        text.transform.SetParent(crystal.transform, false);
        text.transform.localScale = Vector3.zero;
        text.transform.localRotation = Quaternion.Euler(0, 270, 270);

        crystal.titleText = text.GetComponent<TextMeshPro>();
        crystal.titleText.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 2);
        crystal.titleText.horizontalAlignment = HorizontalAlignmentOptions.Center;
        crystal.titleText.ForceMeshUpdate();

        var lookAt = text.AddComponent<LookAtPlayer>();
        lookAt.lockX = true;
        lookAt.lockZ = true;
                
        crystal.ReplayPath = ReplayFiles.explorer.CurrentReplayPath;
        
        ReplayFiles.explorer.Next();

        if (applyRandomColor)
        {
            crystal.BaseColor = Utilities.RandomColor();
            crystal.ApplyVisuals();
        }
        
        crystal.gameObject.SetActive(true);
                
        Crystals.Add(crystal);

        if (useAnimation)
            MelonCoroutines.Start(CrystalSpawnAnimation(crystal));

        return crystal;
    }

    public static Crystal CreateCrystal()
    {
        if (crystalParent == null)
            crystalParent = new GameObject("Crystals");
        
        Crystal crystal = GameObject.Instantiate(crystalPrefab, crystalParent.transform).AddComponent<Crystal>();
        crystal.hasLeftTable = true;
        crystal.gameObject.SetActive(true);

        GameObject text = Create.NewText("", 1f, Color.white, Vector3.zero, Quaternion.identity);

        text.name = "Replay Title";
        text.transform.SetParent(crystal.transform, false);
        text.transform.localScale = Vector3.zero;
        text.transform.localRotation = Quaternion.Euler(0, 270, 270);

        crystal.titleText = text.GetComponent<TextMeshPro>();
        crystal.titleText.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 2);
        crystal.titleText.horizontalAlignment = HorizontalAlignmentOptions.Center;
        crystal.titleText.ForceMeshUpdate();
        
        var lookAt = text.AddComponent<LookAtPlayer>();
        lookAt.lockX = true;
        lookAt.lockZ = true;
        
        Crystals.Add(crystal);

        return crystal;
    }

    public static IEnumerator ReadCrystal(Crystal crystal)
    {
        crystal.isAnimation = true;
        
        lastReplayColor = (crystal.ReplayPath, crystal.BaseColor);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_GearMarket_ButtonUnpress"], crystal.transform.position);

        yield return Utilities.LerpValue(
            () => crystal.transform.position,
            v => crystal.transform.position = v,
            Vector3.Lerp,
            Main.instance.replayTable.transform.position + new Vector3(0, 0.3045f, 0),
            1f,
            Utilities.EaseInOut
        );
        
        yield return new WaitForSeconds(1f);

        AudioManager.instance.Play(ReplayCache.SFX["Call_Phone_ScreenDown"], crystal.transform.position);
        
        yield return Utilities.LerpValue(
            () => crystal.BaseColor,
            v =>
            {
                crystal.BaseColor = v;
                crystal.ApplyVisuals();
            },
            Color32.Lerp,
            new(50, 50, 50, 255),
            1f,
            Utilities.EaseInOut
        );

        yield return new WaitForSeconds(0.5f);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_ToolTip_Close"], crystal.transform.position);

        MelonCoroutines.Start(Utilities.LerpValue(
            () => crystal.transform.position,
            v => crystal.transform.position = v,
            Vector3.Lerp,
            Main.instance.replayTable.transform.position,
            0.5f,
            Utilities.EaseInOut
        ));

        yield return Utilities.LerpValue(
            () => crystal.transform.localScale,
            v => crystal.transform.localScale = v,
            Vector3.Lerp,
            Vector3.zero,
            0.5f,
            Utilities.EaseInOut,
            () =>
            {
                int index = ReplayFiles.explorer.currentReplayPaths.IndexOf(crystal.ReplayPath);
                
                if (index != -1)
                    ReplayFiles.SelectReplay(index);
                else
                    AudioManager.instance.Play(ReplayCache.SFX["Call_Measurement_Failure"], crystal.transform.position);
                
                Crystals.Remove(crystal);
                GameObject.Destroy(crystal.gameObject);
                SaveCrystals();
            }
        );
    }

    public static IEnumerator CrystalBreakAnimation(string replayPath, Crystal crystal = null, float dist = 0.005f)
    {
        AudioManager.instance.Play(ReplayCache.SFX["Call_GearMarket_ButtonUnpress"], Main.instance.replayTable.transform.position);

        crystalizeVFX.transform.localPosition = new Vector3(0, 0, 0.3903f);
        
        bool isNewCrystal = false;
        if (crystal == null)
        {
            isNewCrystal = true;
            crystal = CreateCrystal();
            crystal.transform.position = Main.instance.replayTable.transform.position;
            crystal.transform.localScale = Vector3.zero;
            crystal.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            crystal.BaseColor = Utilities.RandomColor();
            crystal.ApplyVisuals();
            
            MelonCoroutines.Start(Utilities.LerpValue(
                () => crystal.transform.localScale,
                v => crystal.transform.localScale = v,
                Vector3.Lerp,
                Vector3.one * 50f,
                1f,
                Utilities.EaseInOut
            ));
        }

        crystal.isAnimation = true;

        if (isNewCrystal)
        {
            MelonCoroutines.Start(Utilities.LerpValue(
                () => crystal.transform.position,
                v => crystal.transform.position = v,
                Vector3.Lerp,
                Main.instance.replayTable.transform.position + new Vector3(0, 0.4045f, 0),
                1.5f,
                Utilities.EaseInOut
            ));
        }
        else
        {
            yield return Utilities.LerpValue(
                () => crystal.transform.position,
                v => crystal.transform.position = v,
                Vector3.Lerp,
                Main.instance.replayTable.transform.position + new Vector3(0, 0.4045f, 0),
                1.5f,
                Utilities.EaseInOut
            );
        }
        
        yield return new WaitForSeconds(0.2f);

        MelonCoroutines.Start(SpinEaseInOut(crystal.transform, 360, 2.4f, Vector3.forward));

        yield return new WaitForSeconds(1f);

        AudioManager.instance.Play(ReplayCache.SFX["Call_FistBumpBonus"], crystal.transform.position);
        AudioManager.instance.Play(ReplayCache.SFX["Call_Slab_Dismiss"], crystal.transform.position);
        AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_Use"], crystal.transform.position);

        Main.instance.replayTable.transform.GetChild(7).GetComponent<VisualEffect>().Play();

        if (crystal.TryGetComponent<Renderer>(out var mainRenderer))
            mainRenderer.enabled = false;

        foreach (var shard in crystal.GetComponentsInChildren<Transform>(true))
        {
            if (shard == crystal.transform) continue;
            
            shard.gameObject.SetActive(true);

            Vector3 target = shard.localPosition.normalized * dist;
            MelonCoroutines.Start(Utilities.LerpValue(
                () => shard.localPosition,
                v => shard.localPosition = v,
                Vector3.Lerp,
                target,
                1f,
                Utilities.EaseOut
            ));
        }

        yield return new WaitForSeconds(0.948f);
        
        foreach (var shard in crystal.GetComponentsInChildren<Transform>(true))
        {
            if (shard == crystal.transform) continue;

            MelonCoroutines.Start(Utilities.LerpValue(
                () => shard.localScale,
                v => shard.localScale = v,
                Vector3.Lerp,
                Vector3.zero,
                0.5f,
                Utilities.EaseIn,
                isValid: () => shard != null,
                delay: Random.value,
                done: () => AudioManager.instance.Play(ReplayCache.SFX["Call_DressingRoom_PartPanelTick_BackwardLocked"], shard.transform.position)
            ));
        }

        yield return new WaitForSeconds(1f);

        Crystals.Remove(crystal);
        GameObject.Destroy(crystal);
        SaveCrystals();

        File.Delete(replayPath);
        ReplayAPI.ReplayDeletedInternal(replayPath);
        ReplayFiles.ReloadReplays();

        Main.instance.crystalBreakCoroutine = null;
    }

    public static IEnumerator SpinEaseInOut(Transform target, float maxSpeed, float duration, Vector3 axis)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float eased = 6 * t * (1 - t);
            float spinSpeed = maxSpeed * eased;

            target.Rotate(axis, spinSpeed * Time.deltaTime, Space.Self);

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    public static IEnumerator CrystalSpawnAnimation(Crystal crystal)
    {
        crystal.isAnimation = true;
        
        crystal.transform.localScale = Vector3.zero;
        crystal.transform.position = Main.instance.replayTable.transform.position;

        crystalizeVFX.transform.localPosition = new Vector3(0, 0, 0.3045f);

        crystal.BaseColor = new (50, 50, 50, 255);
        crystal.ApplyVisuals();

        AudioManager.instance.Play(ReplayCache.SFX["Call_MoveSelector_Unlock"], crystal.transform.position);

        MelonCoroutines.Start(Utilities.LerpValue(
            () => crystal.transform.position,
            v => crystal.transform.position = v,
            Vector3.Lerp,
            Main.instance.replayTable.transform.position + new Vector3(0, 0.3045f, 0),
            1.5f,
            Utilities.EaseInOut
        ));

        yield return Utilities.LerpValue(
            () => crystal.transform.localScale,
            v => crystal.transform.localScale = v,
            Vector3.Lerp,
            Vector3.one * 50f,
            1.5f,
            Utilities.EaseInOut
        );

        yield return new WaitForSeconds(1f);
        
        AudioManager.instance.Play(ReplayCache.SFX["Call_Shiftstone_Use"], crystal.transform.position);
        
        crystalizeVFX.Play();

        MelonCoroutines.Start(Utilities.LerpValue(
            () => crystal.BaseColor,
            v =>
            {
                crystal.BaseColor = v;
                crystal.ApplyVisuals();
            },
            Color32.Lerp,
            !string.IsNullOrEmpty(lastReplayColor.path) && crystal.ReplayPath == lastReplayColor.path ? lastReplayColor.color : Utilities.RandomColor(),
            0.05f,
            Utilities.EaseInOut
        ));
        

        yield return Utilities.LerpValue(
            () => crystal.transform.localRotation,
            v => crystal.transform.localRotation = v,
            Quaternion.Slerp,
            Quaternion.Euler(290, 0, 0),
            0.05f
        );

        yield return Utilities.LerpValue(
            () => crystal.transform.localRotation,
            v => crystal.transform.localRotation = v,
            Quaternion.Slerp,
            Quaternion.Euler(270, 0, 0),
            0.7f,
            Utilities.EaseInOut
        );

        crystal.isAnimation = false;
    }
    
    [RegisterTypeInIl2Cpp]
    public class Crystal : MonoBehaviour
    {
        public string ReplayPath;
        
        public string Title;
        public TextMeshPro titleText;

        public Color32 BaseColor;

        private Renderer rend;
        private MaterialPropertyBlock mpb;
        
        public bool isGrabbed;
        public bool isAnimation;
        public bool hasLeftTable;

        private Vector3 basePosition;
        private Vector3 velocity;
        private Vector3 lastPosition;

        private Vector3 angularVelocity;
        private Quaternion lastRotation;
        
        private bool hasSavedAfterRelease;

        private object scaleRoutine;
        private object positionRoutine;
        private bool isTextVisible;

        private void Awake()
        {
            basePosition = transform.position;
            lastPosition = transform.position;
        }
        
        public CrystalState CaptureState()
        {
            return new CrystalState
            {
                ReplayPath = ReplayPath,
                Title = Title,
                x = transform.position.x,
                y = transform.position.y,
                z = transform.position.z,
                BaseColor = BaseColor
            };
        }

        public void RestoreState(CrystalState state)
        {
            ReplayPath = state.ReplayPath;
            BaseColor = state.BaseColor;

            basePosition = new Vector3(state.x, state.y, state.z);
            transform.position = basePosition;

            Title = state.Title;
            titleText.text = state.Title;
            titleText.ForceMeshUpdate();
            
            ApplyVisuals();
        }
        
        public void ApplyVisuals()
        {
            Color baseColor = BaseColor;

            if (rend == null)
                rend = GetComponent<Renderer>();

            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            ApplyBlockToRenderer(rend, baseColor);

            foreach (var childRenderer in GetComponentsInChildren<Renderer>(true))
            {
                if (childRenderer == rend) continue;
                ApplyBlockToRenderer(childRenderer, baseColor);
            }
        }

        private void ApplyBlockToRenderer(Renderer r, Color baseColor)
        {
            if (r == null) return;

            mpb.Clear();
            mpb.SetColor("_Base_Color", baseColor);
            mpb.SetColor("_Edge_Color", DeriveEdge(baseColor));
            mpb.SetColor("_Shadow_Color", DeriveShadow(baseColor));
            r.SetPropertyBlock(mpb);
        }
        
        public void ShowText()
        {
            Animate(Vector3.one * 0.02f, new Vector3(0f, 0f, 0.0049f));
            isTextVisible = true;
        }

        public void HideText()
        {
            Animate(Vector3.zero, Vector3.zero);
            isTextVisible = false;
        }

        void Animate(Vector3 scale, Vector3 position)
        {
            if (scaleRoutine != null)
                MelonCoroutines.Stop(scaleRoutine);

            scaleRoutine = MelonCoroutines.Start(
                Utilities.LerpValue(
                    () => titleText.transform.localScale,
                    v => titleText.transform.localScale = v,
                    Vector3.Lerp,
                    scale,
                    0.5f,
                    Utilities.EaseInOut,
                    isValid: () => titleText.transform != null
                )
            );

            if (positionRoutine != null)
                MelonCoroutines.Stop(positionRoutine);

            positionRoutine = MelonCoroutines.Start(
                Utilities.LerpValue(
                    () => titleText.transform.localPosition,
                    v => titleText.transform.localPosition = v,
                    Vector3.Lerp,
                    position,
                    0.5f,
                    Utilities.EaseInOut,
                    isValid: () => titleText.transform != null
                )
            );
        }

        public void Grab()
        {
            if (isGrabbed || isAnimation)
                return;

            isGrabbed = true;
            hasLeftTable = true;
            hasSavedAfterRelease = false;
            velocity = Vector3.zero;

            HideText();
        }

        public void Release()
        {
            if (!isGrabbed)
                return;

            isGrabbed = false;
        }
        
        void Update()
        {
            if (!isAnimation)
            {
                if (!isGrabbed)
                {
                    ApplyThrowVelocity();
                    HandleProximity();
                }
                else
                {
                    basePosition = transform.position;
                    velocity = (transform.position - lastPosition) / Time.deltaTime;
                    
                    Quaternion current = transform.rotation;
                    Quaternion delta = current * Quaternion.Inverse(lastRotation);

                    delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f)
                        angleDeg -= 360f;

                    angularVelocity = axis * (angleDeg * Mathf.Deg2Rad) / Time.deltaTime;

                    lastRotation = current;
                }

                lastPosition = transform.position;
            }
            else
            {
                HideText();
            }
        }
        

        void HandleProximity()
        {
            var head = Main.instance.head;
            if (head == null)
                return;

            float dist = Vector3.Distance(head.position, transform.position);

            const float showDist = 2f;
            const float hideDist = 2.2f;

            if (!isTextVisible && dist < showDist)
            {
                isTextVisible = true;
                ShowText();
            } else if (isTextVisible && dist > hideDist)
            {
                isTextVisible = false;
                HideText();
            }
        }

        void ApplyThrowVelocity()
        {
            if (velocity.sqrMagnitude < 0.001f && angularVelocity.sqrMagnitude < 0.001f)
            {
                if (!hasSavedAfterRelease)
                {
                    SaveCrystals();
                    hasSavedAfterRelease = true;
                }

                velocity = Vector3.zero;
                return;
            }

            basePosition += velocity * Time.deltaTime;
            velocity = Vector3.Lerp(velocity, Vector3.zero, 8f * Time.deltaTime);
            transform.position = basePosition;

            float angle = angularVelocity.magnitude * Time.deltaTime;
            if (angle > 0f)
                transform.rotation = Quaternion.AngleAxis(angle * Rad2Deg, angularVelocity.normalized) * transform.rotation;
            
            angularVelocity = Vector3.Lerp(angularVelocity, Vector3.zero, 8f * Time.deltaTime);

            if (angularVelocity.sqrMagnitude < 0.0005f)
            {
                float a = Quaternion.Angle(transform.rotation, Quaternion.Euler(-90f, 0, 0));
                float b = Quaternion.Angle(transform.rotation, Quaternion.Euler(90f, 0, 0));

                if (a < 5f || b < 5f)
                {
                    Quaternion target = a < b ? Quaternion.Euler(-90f, 0, 0) : Quaternion.Euler(90f, 0, 0);

                    transform.rotation = Quaternion.Slerp(transform.rotation, target, 8f * Time.deltaTime);
                }
            }
        }

        static Color DeriveEdge(Color baseColor)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Clamp01(s + 0.05f), Clamp01(v + 0.35f));
        }

        static Color DeriveShadow(Color baseColor)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Clamp01(s - 0.25f), Clamp01(v - 0.4f));
        }
    }

    [Serializable]
    public struct CrystalState
    {
        public string ReplayPath;
        public string Title;

        public float x;
        public float y;
        public float z;
        
        public Color32 BaseColor;
    }
}