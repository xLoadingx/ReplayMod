using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppRUMBLE.Environment.MatchFlow;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using MelonLoader;
using ReplayMod.Core;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using UnityEngine;
using Random = UnityEngine.Random;
using static UnityEngine.Mathf;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay;

public static class Utilities
{
    private static GameObject customMultiplayerMaps;
    
    public static string GetFriendlySceneName(string scene)
    {
        return scene switch
        {
            "Map0" => "Ring",
            "Map1" => "Pit",
            _ => scene
        };
    }

    public static string GetActiveCustomMapName()
    {
        customMultiplayerMaps ??= GameObject.Find("CustomMultiplayerMaps");

        if (customMultiplayerMaps == null)
            return null;

        for (int i = 0; i < customMultiplayerMaps.transform.childCount; i++)
        {
            var child = customMultiplayerMaps.transform.GetChild(i);
            if (child.gameObject.activeInHierarchy)
                return child.name;
        }

        return null;
    }

    public static string[] RebuildCustomMapFromScene()
    {
        var parent = GameObject.Find("CustomMapParent");
        if (parent == null) return null;

        List<string> data = new List<string>();
        data.Add("1");
        data.Add(parent.transform.childCount.ToString());

        for (int i = 0; i < parent.transform.childCount; i++)
        {
            var child = parent.transform.GetChild(i);

            string name = child.name;

            Color color = Color.white;
            var renderer = child.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null && renderer.material.HasProperty("_Color"))
                color = renderer.material.color;

            Vector3 pos = child.position;
            Vector3 rot = child.rotation.eulerAngles;
            Vector3 scale = child.localScale;

            data.Add(name);
            data.Add(color.r.ToString(CultureInfo.InvariantCulture));
            data.Add(color.g.ToString(CultureInfo.InvariantCulture));
            data.Add(color.b.ToString(CultureInfo.InvariantCulture));
            
            data.Add(pos.x.ToString(CultureInfo.InvariantCulture));
            data.Add(pos.y.ToString(CultureInfo.InvariantCulture));
            data.Add(pos.z.ToString(CultureInfo.InvariantCulture));
            
            data.Add(rot.x.ToString(CultureInfo.InvariantCulture));
            data.Add(rot.y.ToString(CultureInfo.InvariantCulture));
            data.Add(rot.z.ToString(CultureInfo.InvariantCulture));
            
            data.Add(scale.x.ToString(CultureInfo.InvariantCulture));
            data.Add(scale.y.ToString(CultureInfo.InvariantCulture));
            data.Add(scale.z.ToString(CultureInfo.InvariantCulture));
        }

        return data.ToArray();
    }

    public static GameObject GetCustomMap(string mapName)
    {
        customMultiplayerMaps ??= GameObject.Find("CustomMultiplayerMaps");

        if (customMultiplayerMaps == null)
        {
            Main.ReplayError("Selected replay uses a custom map, but custom maps are not installed.");
            return null;
        }

        GameObject map = customMultiplayerMaps.transform.Find(mapName)?.gameObject;

        if (map == null)
        {
            Main.ReplayError($"Could not find the custom map '{mapName}.");
            return null;
        }

        return map;
    }

    public static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";

        string s = Regex.Replace(name, "<.*?>", string.Empty);

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!char.IsSurrogate(ch))
                sb.Append(ch);
        }

        s = sb.ToString();

        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c.ToString(), "");

        s = Regex.Replace(s, @"\s+", " ").Trim();

        s = s.Replace("\\", "_").Replace("/", "_");
        
        return string.IsNullOrEmpty(s) ? "Unknown" : s;
    }
    

    // ----- Replay Helpers -----
    
    public static bool IsReplayClone(PlayerController controller)
    {
        if (controller == null || Main.Playback.PlaybackPlayers == null)
            return false;
        
        foreach (var clone in Main.Playback.PlaybackPlayers)
            if (clone.Controller == controller)
                return true;

        return false;
    }
    
    public static IEnumerable<GameObject> EnumerateMatchPedestals()
    {
        return GameObject.FindObjectsOfType<Pedestal>(true).Select(p => p.gameObject);
    }
    
    public static IEnumerator LoadMap(int index, float fadeDuration = 2f, Action onLoaded = null, float onLoadedDelay = 0.01f)
    {
        CombatManager.instance.CleanStructureList();
        
        foreach (var structure in CombatManager.instance.structures.ToArray())
            structure?.Kill(Vector3.zero, false, false);
        
        SceneManager.instance.LoadSceneAsync(index, false, false, fadeDuration);

        while (SceneManager.instance.IsLoadingScene)
            yield return null;

        yield return new WaitForSeconds(onLoadedDelay);
        onLoaded?.Invoke();
    }
    
    public static bool HasVFXType(string type, Transform obj)
    {
        var tags = obj.GetComponentsInChildren<ReplayPlayback.ReplayTag>();
        foreach (var t in tags)
        {
            if (t.Type == type)
                return true;
        }
        return false;
    }
    
    public static Vector3 GetPositionOverMesh(float a, float b, MeshRenderer renderer)
    {
        float u = Clamp01(a / b);

        Bounds localBounds = renderer.localBounds;

        float localX = Lerp(localBounds.min.x, localBounds.max.x, u);

        Vector3 localPos = localBounds.center;
        localPos.x = localX;
        return renderer.transform.TransformPoint(localPos);
    }

    public static float GetProgressFromMeshPosition(Vector3 worldPos, MeshRenderer renderer)
    {
        Vector3 localPos = renderer.transform.InverseTransformPoint(worldPos);

        Bounds localBounds = renderer.localBounds;

        float minX = localBounds.min.x;
        float maxX = localBounds.max.x;

        if (Approximately(maxX, minX))
            return 0f;

        float u = InverseLerp(minX, maxX, localPos.x);
        return Clamp01(u);
    }
    
    public static Marker[] AddMarkers(ReplaySerializer.ReplayHeader header, MeshRenderer timelineRenderer, bool hideMarkers = true)
    {
        foreach (var marker in timelineRenderer.transform.GetComponentsInChildren<ReplayPlayback.ReplayTag>())
            GameObject.Destroy(marker.gameObject);
        
        if (header.Markers == null)
            return null;
        
        var markers = header.Markers;
        
        foreach (var marker in markers)
        {
            Vector3 position = GetPositionOverMesh(marker.time, header.Duration, timelineRenderer);
            GameObject markerObj = GameObject.Instantiate(ReplayPlaybackControls.markerPrefab, timelineRenderer.transform);

            if (!hideMarkers)
                markerObj.layer = LayerMask.NameToLayer("Default");

            markerObj.transform.localScale = new Vector3(0.0062f, 1.0836f, 0.0128f);
            markerObj.transform.position = position;

            Color markerColor = new Color(marker.r, marker.g, marker.b, 1f);
            markerObj.GetComponent<MeshRenderer>().material.SetColor("_Overlay", markerColor);
            markerObj.AddComponent<ReplayPlayback.ReplayTag>();
            markerObj.SetActive(true);
        }

        return markers;
    }

    public static string FormatBytes(long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;

        if (bytes >= MB)
            return $"{bytes / MB:0.##} MB";

        if (bytes >= KB)
            return $"{bytes / KB:0.##} KB";

        return $"{bytes} B";
    }

    // ----- Lerping -----
    
    public static float EaseInOut(float t) => t < 0.5f ? 2 * t * t : 1 - Pow(-2 * t + 2, 2) / 2;
    public static float EaseOut(float t) => 1 - Pow(1 - t, 4);
    public static float EaseIn(float t) => Pow(t, 3);
    
    public static IEnumerator LerpValue<T>(
        Func<T> getter,
        Action<T> setter,
        Func<T, T, float, T> lerpFunc,
        T targetValue,
        float duration,
        Func<float, float> easing = null,
        Action done = null,
        float delay = 0f,
        Func<bool> isValid = null
    )
    {
        yield return new WaitForSeconds(delay);
        
        T startValue = getter();
        float t = 0f;

        while (t < 1f)
        {
            if (isValid != null && !isValid())
                yield break;
            
            t += Time.deltaTime / duration;
            
            float easedT = easing?.Invoke(Clamp01(t)) ?? t;
            setter(lerpFunc(startValue, targetValue, easedT));
            yield return null;
        }

        setter(targetValue);
        done?.Invoke();
    }
    
    
    // ----- Other -----
    
    public static Color32 RandomColor()
    {
        float h = Random.value;
        float s = Random.Range(0.6f, 0.9f);
        float v = Random.Range(0.7f, 1.0f);
        
        Color c = Color.HSVToRGB(h, s, v);
        return c;
    }
    
    public static T[] NewArray<T>(int count, T[] copyFrom = null) where T : new()
    {
        var arr = new T[count];
        for (int i = 0; i < count; i++)
            arr[i] = copyFrom != null && i < copyFrom.Length
                ? copyFrom[i]
                : new T();
        return arr;
    }
}

[RegisterTypeInIl2Cpp]
public class LookAtPlayer : MonoBehaviour
{
    public bool lockX;
    public bool lockY;
    public bool lockZ;

    void Update()
    {
        transform.rotation = Quaternion.Euler(0, 270, 0);
        
        var cam = Camera.main;
        if (!cam)
            return;

        Vector3 toCam = cam.transform.position - transform.position;
        if (toCam.sqrMagnitude < 0.0001f)
            return;

        Quaternion target = Quaternion.LookRotation(toCam, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);

        Vector3 targetEuler = target.eulerAngles;
        Vector3 currentEuler = transform.rotation.eulerAngles;

        if (lockX) targetEuler.x = currentEuler.x;
        if (lockY) targetEuler.y = currentEuler.y;
        if (lockZ) targetEuler.z = currentEuler.z;

        transform.rotation = Quaternion.Euler(targetEuler);
    }
}

[RegisterTypeInIl2Cpp]
public class DeleteAfterSeconds : MonoBehaviour
{
    public float destroyTime = 10f;
    private float spawnTime;

    public void Awake()
    {
        spawnTime = Main.Playback.elapsedPlaybackTime;
    }

    public void Update()
    {
        if (Abs(Main.Playback.elapsedPlaybackTime - spawnTime) >= destroyTime)
            Destroy(gameObject);
    }
}