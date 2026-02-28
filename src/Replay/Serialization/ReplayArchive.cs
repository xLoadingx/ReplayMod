using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using MelonLoader;
using Newtonsoft.Json;
using ReplayMod.Replay.Files;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.Replay.Serialization;

public class ReplayArchive
{
    public static async Task BuildReplayPackage(
        string outputPath, 
        ReplayInfo replay, 
        Action done = null
    )
    {
        try
        {
            byte[] rawReplay = ReplaySerializer.SerializeReplayFile(replay);
            
            Main.instance.LoggerInstance.Msg($"Replay data serialized ({Utilities.FormatBytes(rawReplay.Length)})");

            byte[] compressedReplay = await Task.Run(() => ReplayCodec.Compress(rawReplay));

            Main.instance.LoggerInstance.Msg($"Compression complete ({Utilities.FormatBytes(rawReplay.Length)} -> {Utilities.FormatBytes(compressedReplay.Length)}, " +
                                             $"{100 - (compressedReplay.Length * 100.0 / rawReplay.Length):0.#}% reduction).");
            
            MelonCoroutines.Start(FinishOnMainThread(outputPath, replay, compressedReplay, done));
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
        byte[] compressedReplay,
        Action done
    )
    {
        yield return null;
        
        string manifestJson = JsonConvert.SerializeObject(
            replay.Header,
            Formatting.Indented,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
        );
        
        using (var fs = new FileStream(outputPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(manifestJson);

            var replayEntry = zip.CreateEntry("replay", CompressionLevel.NoCompression);
            using (var stream = replayEntry.Open())
                stream.Write(compressedReplay, 0, compressedReplay.Length);

            ReplayAPI.InvokeArchiveBuild(zip);
        }

        done?.Invoke();
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
        
        using var fs = new FileStream(replayPath, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        
        var manifestEntry = zip.GetEntry("manifest.json");

        manifestEntry?.Delete();

        var newEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        
        using var writer = new StreamWriter(newEntry.Open());
        writer.Write(JsonConvert.SerializeObject(
            header,
            Formatting.Indented
        ));
        
        ReplayFiles.suppressWatcher = false;
    }
}