using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppPhoton.Voice;
using Il2CppPhoton.Voice.PUN;
using Il2CppPhoton.Voice.Unity;
using Il2CppRUMBLE.Managers;
using Il2CppSystem;
using MelonLoader;
using MelonLoader.Utils;
using NAudio.Wave;
using ReplayMod.Replay.Serialization;
using UnityEngine;

namespace ReplayMod.Replay;

// Voice recording - UNUSED
// Not enabled due to Il2CPP codec limitaitons and privacy concerns
// Kept for future ideas
internal static class ReplayVoices
{
    private static PunVoiceClient voice;

    private static readonly Dictionary<(int playerId, int voiceId), VoiceStreamWriter> writers = new();

    public static string TempVoiceDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ReplayMod", "TempVoices");

    public static List<VoiceTrackInfo> VoiceTrackInfos = new();

    private static bool isRecording;
    private static bool subscribed;

    public static void StartRecording()
    {
        isRecording = true;
        
        voice ??= PunVoiceClient.Instance;

        if (voice is not null)
        {
            if (!subscribed)
            {
                voice.RemoteVoiceAdded += (Action<RemoteVoiceLink>)OnVoiceLinkAdded;
                subscribed = true;
            }
            
            foreach (var remoteVoiceLink in voice.cachedRemoteVoices)
                OnVoiceLinkAdded(remoteVoiceLink);
            
            Directory.CreateDirectory(TempVoiceDir);
        }
    }

    public static void OnVoiceLinkAdded(RemoteVoiceLink link)
    {
        int playerId = link.PlayerId;
        int voiceId = link.VoiceId;

        MelonLogger.Msg($"VoiceAdded actor={playerId} voice={voiceId}");

        string name = PlayerManager.instance.AllPlayers.ToArray()
            .FirstOrDefault(p => p.Data.GeneralData.ActorNo == playerId)?
            .Data.GeneralData.PublicUsername
            ?? "Unknown";

        name = Utilities.CleanName(name);

        var key = (playerId, voiceId);

        link.FloatFrameDecoded += (Action<FrameOut<float>>)((FrameOut<float> frame) =>
        {
            if (!isRecording)
                return;
            
            if (!writers.TryGetValue(key, out var writer))
            {
                string fileName = $"{name}_actor_{playerId}_voice_{voiceId}_{Time.frameCount}.wav";
                string path = Path.Combine(TempVoiceDir, fileName);

                writer = new VoiceStreamWriter(
                    playerId,
                    link.VoiceInfo.SamplingRate,
                    link.VoiceInfo.Channels,
                    path
                );

                writers[key] = writer;

                VoiceTrackInfos.Add(new VoiceTrackInfo(
                    playerId,
                    fileName,
                    Time.time
                ));

                MelonLogger.Msg($"VoiceClipStart actor={playerId} voice={voiceId}");
            }

            writer.Write(frame.Buf);

            if (frame.EndOfStream)
            {
                MelonLogger.Msg($"VoiceClipEnd actor={playerId} voice={voiceId}");
                StopWriter(key);
            }
        });

        link.RemoteVoiceRemoved += (Action)(() =>
        {
            MelonLogger.Msg($"VoiceRemoved actor={playerId} voice={voiceId}");
            StopWriter(key);
        });
    }

    public static void StopRecording()
    {
        isRecording = false;
        
        foreach (var key in writers.Keys.ToList())
            StopWriter(key);

        MergeVoiceClips();

        VoiceTrackInfos.Clear();
    }

    private static void MergeVoiceClips()
    {
        if (VoiceTrackInfos.Count == 0)
            return;

        var groups = VoiceTrackInfos.GroupBy(v => v.ActorId);

        foreach (var group in groups)
        {
            var sorted = group.OrderBy(v => v.StartTime).ToList();
            
            string outputPath = Path.Combine(TempVoiceDir, $"actor_{group.Key}_merged.wav");

            int sampleRate = 48000;
            int channels = 1;

            using var output = new WaveFileWriter(
                outputPath,
                WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)
            );

            float lastTime = sorted[0].StartTime;

            foreach (var clip in sorted)
            {
                string path = Path.Combine(TempVoiceDir, clip.FileName);

                if (!File.Exists(path))
                    continue;
            
                using var reader = new WaveFileReader(path);

                float gap = clip.StartTime - lastTime;

                if (gap > 0)
                {
                    int silentSamples = (int)(gap * sampleRate);
                    float[] silence = new float[silentSamples];
                    output.WriteSamples(silence, 0, silence.Length);
                }
            
                var sampleProvider = reader.ToSampleProvider();
                float[] buffer = new float[sampleRate];

                int read;
                while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.WriteSamples(buffer, 0, read);
                }

                lastTime = clip.StartTime + (float)reader.TotalTime.TotalSeconds;
            }
        }
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

        private readonly FileStream fileStream;
        private readonly WaveFileWriter wav;

        public VoiceStreamWriter(
            int actorId,
            int sampleRate,
            int channels,
            string path
        )
        {
            ActorId = actorId;
            Path = path;

            fileStream = File.Create(path);
            wav = new WaveFileWriter(
                fileStream,
                WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)
            );
        }

        public void Write(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return;

            HasFrames = true;
            wav.WriteSamples(samples, 0, samples.Length);
        }

        public void Dispose()
        {
            wav.Dispose();
            fileStream?.Dispose();
        }
    }
}