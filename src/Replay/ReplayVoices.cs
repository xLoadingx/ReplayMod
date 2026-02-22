using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppPhoton.Voice;
using Il2CppPhoton.Voice.PUN;
using Il2CppPhoton.Voice.Unity;
using Il2CppRUMBLE.Managers;
using MelonLoader.Utils;
using ReplayMod.Replay.Serialization;
using UnityEngine;

namespace ReplayMod.Replay;

// Voice recording - UNUSED
// Not enabled due to Il2CPP codec limitaitons and privacy concerns
// Kept for future ideas
internal static class ReplayVoices
{
    // Remote
    private static PunVoiceClient voice;
    
    private static readonly Dictionary<(int playerId, int voiceId), VoiceStreamWriter> writers = new();
    public static string tempVoiceDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ReplayMod", "TempVoices");
    
    public static List<VoiceTrackInfo> voiceTrackInfos = new();

    public static void HookRemote()
    {
        voice ??= PunVoiceClient.Instance;
    
        voice.RemoteVoiceAdded += (Il2CppSystem.Action<RemoteVoiceLink>)(OnRemoteVoiceAdded);
    
        Directory.CreateDirectory(tempVoiceDir);
    }
    
    public static void OnRemoteVoiceAdded(RemoteVoiceLink link)
    {
        int playerId = link.PlayerId;
        int voiceId = link.VoiceId;

        string name = PlayerManager.instance.AllPlayers.ToArray()
                .FirstOrDefault(p => p.Data.GeneralData.ActorNo == playerId)?
                .Data.GeneralData.PublicUsername
            ?? $"Unknown";

        name = Utilities.CleanName(name);

        string fileName = $"{name}_actor_{playerId}_voice_{voiceId}.ogg";
        
        voiceTrackInfos.Add(new VoiceTrackInfo
        {
            ActorId = playerId,
            FileName = fileName,
            StartTime = Time.time
        });

        string path = Path.Combine(
            tempVoiceDir,
            fileName
        );

        var writer = new VoiceStreamWriter(
            playerId,
            link.VoiceInfo.SamplingRate,
            link.VoiceInfo.Channels,
            path
        );

        var key = (playerId, voiceId);
        writers[key] = writer;

        link.FloatFrameDecoded += (Il2CppSystem.Action<FrameOut<float>>)((FrameOut<float> frame) =>
        {
            if (!writers.TryGetValue(key, out var w))
                return;

            w.Write(frame.Buf);

            if (frame.EndOfStream)
                StopWriter(key);
        });

        link.RemoteVoiceRemoved += (Il2CppSystem.Action)(() =>
        {
            StopWriter(key);
        });
    }

    private static void StopWriter((int playerId, int voiceId) key)
    {
        if (!writers.Remove(key, out var writer))
            return;

        writer.Dispose();

        if (!writer.HasFrames && File.Exists(writer.Path))
            File.Delete(writer.Path);
    }

    public class VoiceStreamWriter
    {
        public readonly int ActorId;
        public readonly string Path;
        public bool HasFrames { get; private set; }

        private readonly FileStream file;
        // private readonly OpusOggWriteStream ogg;

        public VoiceStreamWriter(
            int actorId,
            int sampleRate,
            int channels,
            string path
        )
        {
            ActorId = actorId;
            Path = path;

            file = File.Create(path);
            
            // Causes system.runtime error
            // var encoder = new OpusEncoder(
            //     sampleRate,
            //     channels,
            //     OpusApplication.OPUS_APPLICATION_VOIP
            // );
            
            // encoder.Bitrate = 24000;
            // encoder.UseVBR = true;
            // encoder.UseDTX = true;
            // encoder.Complexity = 6;
            //
            // ogg = new OpusOggWriteStream(encoder, file);
        }

        public void Write(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return;

            HasFrames = true;
            // ogg.WriteSamples(samples, 0, samples.Length);
        }

        public void Dispose()
        {
            // ogg.Finish();
            file?.Dispose();
        }
    }
}