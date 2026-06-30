using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Il2CppSystem.Diagnostics;
using MelonLoader;
using Newtonsoft.Json;
using ReplayMod.Replay.Files;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.Serialization;

public class ReplayArchive
{
    public static IEnumerator BuildReplayPackageSafe(
        string outputPath,
        ReplayInfo replay,
        Action<bool> done
    )
    {
        Main.DebugLog("Safe start");
        
        while (!Main.isSceneReady)
            yield return null;

        Main.DebugLog("Scene ready for save.");
        
        yield return null;
        yield return null;

        Main.DebugLog($"Calling Build Replay Package | Output path: {outputPath}");

        Task task;
        try
        {
            task = BuildReplayPackage(outputPath, replay);
        }
        catch (Exception e)
        {
            Main.ReplayError($"BuildReplayPackage failed before returning task: {e}");
            done?.Invoke(false);
            yield break;
        }
        
        while (!task.IsCompleted)
            yield return null;

        done?.Invoke(true);
    }
    
    public static async Task BuildReplayPackage(
        string outputPath, 
        ReplayInfo replay
    )
    {
        try
        {
            CompressionLevel compressionLevel = Main.instance.CompressionLevel.Value;
            
            var sw = Stopwatch.StartNew();

            byte[] rawReplay = ReplaySerializer.SerializeReplayFile(replay);
            long serializeMs = sw.ElapsedMilliseconds;

            sw.Restart();

            byte[] compressedReplay = await Task.Run(() => ReplayCodec.Compress(rawReplay, compressionLevel));
            long compressMs = sw.ElapsedMilliseconds;

            Main.instance.LoggerInstance.Msg($"Serialization / Compression complete ({Utilities.FormatBytes(compressedReplay.Length)} -> {Utilities.FormatBytes(compressedReplay.Length)})");
            Main.instance.LoggerInstance.Msg($"{100 - (compressedReplay.Length * 100.0 / rawReplay.Length):0.#}% reduction (with compression level {compressionLevel.ToString()})");
            Main.instance.LoggerInstance.Msg($"Serialize: {serializeMs}ms, Compress: {compressMs}ms");
            
            MelonCoroutines.Start(FinishOnMainThread(outputPath, replay, compressedReplay));
        }
        catch (Exception ex)
        { 
            Main.ReplayError($"Saving replay to disk failed: {ex}");
            throw;
        }
    }

    static IEnumerator FinishOnMainThread(
        string outputPath,
        ReplayInfo replay,
        byte[] compressedReplay
    )
    {
        string manifestJson = JsonConvert.SerializeObject(
            replay.Header,
            Formatting.Indented,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
        );

        using (var fs = new FileStream(outputPath, FileMode.Create))
        {
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open()))
                    writer.Write(manifestJson);
                
                var replayEntry = zip.CreateEntry("replay", CompressionLevel.NoCompression);
                using (var stream = replayEntry.Open())
                    stream.Write(compressedReplay, 0, compressedReplay.Length);
                
                while (ReplayVoices.HasActiveWriters)
                    yield return null;
                
                WriteVoices(zip);

                ReplayAPI.InvokeArchiveBuild(zip);
            }
        }
        
        ReplayVoices.Cleanup();
    }

    static void WriteVoices(ZipArchive zip)
    {
        if (!Directory.Exists(ReplayVoices.VoiceCacheDir))
            return;

        foreach (var file in Directory.GetFiles(ReplayVoices.VoiceCacheDir))
        {
            var name = Path.GetFileName(file);
            var entry = zip.CreateEntry($"voices/{name}", CompressionLevel.Optimal);

            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(file);

            fileStream.CopyTo(entryStream);
        }

        var json = JsonConvert.SerializeObject(new VoiceArchiveData
        {
            Tracks = ReplayVoices.VoiceTrackInfos
        });

        var metaEntry = zip.CreateEntry("voices.json", CompressionLevel.Optimal);
        using var metaStream = metaEntry.Open();
        using var writer = new StreamWriter(metaStream);
        writer.Write(json);
    }
    
    public static ReplaySerializer.ReplayHeader GetManifest(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        
        var manifestEntry = zip.GetEntry("manifest.json");
        if (manifestEntry == null)
            throw new Exception("Replay does not have valid manifest.json");

        using var stream = manifestEntry.Open();
        using var reader = new StreamReader(stream);

        ReplayAPI.InvokeArchiveRead(zip);
        
        string json = reader.ReadToEnd();
        return JsonConvert.DeserializeObject<ReplaySerializer.ReplayHeader>(json);
    }

    public static void WriteManifest(string replayPath, ReplaySerializer.ReplayHeader header)
    {
        ReplayFiles.suppressWatcher = true;

        try
        {
            using var fs = new FileStream(replayPath, FileMode.Open, FileAccess.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Update);

            zip.GetEntry("manifest.json")?.Delete();

            var newEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);

            using var writer = new StreamWriter(newEntry.Open());
            writer.Write(JsonConvert.SerializeObject(
                header,
                Formatting.Indented
            ));
        }
        finally
        {
            ReplayFiles.suppressWatcher = false; 
        }
    }
}