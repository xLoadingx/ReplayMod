using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Voice;
using Il2CppPhoton.Voice.PUN;
using Il2CppPhoton.Voice.Unity;
using Il2CppRUMBLE.Managers;
using Concentus;
using Concentus.Oggfile;
using MelonLoader.Utils;
using ReplayMod.Core;
using ReplayMod.Replay.Serialization;
using UnityEngine;

namespace ReplayMod.Replay;

// Voice recording - PRERELEASE
public static class ReplayVoices
{
    private static PunVoiceClient voice;

    private static readonly Dictionary<int, VoiceStreamWriter> writers = new();
    public static string TempVoiceDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ReplayMod", "TempVoices");

    public static List<VoiceTrackInfo> VoiceTrackInfos = new();

    private static bool isRecording;
    private static bool subscribed;

    public static void StartRecording()
    {
        isRecording = true;

        if (PunVoiceClient.instance is PunVoiceClient voice)
        {
            if (!subscribed)
            {
                voice.RemoteVoiceAdded += (Il2CppSystem.Action<RemoteVoiceLink>)OnRemoteVoiceLinkAdded;
                subscribed = true;
            }
            if (voice.VoiceClient.LocalVoices.Cast<Il2CppReferenceArray<LocalVoice>>().Length is 0)
                Main.DebugLog("Local voices is empty");
            foreach (var remoteVoiceLink in voice.cachedRemoteVoices)
                OnRemoteVoiceLinkAdded(remoteVoiceLink);

            Directory.CreateDirectory(TempVoiceDir);
        }
    }


    public static void OnRemoteVoiceLinkAdded(RemoteVoiceLink link)
    {
        int playerId = link.PlayerId;
        int voiceId = link.VoiceId;

        Main.DebugLog($"VoiceAdded actor={playerId} voice={voiceId}");

        string name = PlayerManager.instance.AllPlayers.ToArray()
            .FirstOrDefault(p => p.Data.GeneralData.ActorNo == playerId)?
            .Data.GeneralData.PublicUsername
            ?? "Unknown";

        name = Utilities.CleanName(name);

        link.FloatFrameDecoded += (Il2CppSystem.Action<FrameOut<float>>)((FrameOut<float> frame) =>
        {
            if (!isRecording)
                return;

            if (!writers.TryGetValue(voiceId, out var writer))
            {
                string fileName = $"{name}_actor_{playerId}_voice_{voiceId}_{Time.frameCount}.ogg";
                string path = Path.Combine(TempVoiceDir, fileName);

                writer = new VoiceStreamWriter(
                    voiceId,
                    link.VoiceInfo.SamplingRate,
                    link.VoiceInfo.Channels,
                    path
                );

                writers.Add(voiceId, writer);

                VoiceTrackInfos.Add(new VoiceTrackInfo(
                    playerId,
                    voiceId,
                    fileName
                ));

                Main.DebugLog($"VoiceClipStart actor={playerId} voice={voiceId}");
            }

            writer.Write(frame.Buf);

            if (frame.EndOfStream)
            {
                Main.DebugLog($"VoiceClipEnd actor={playerId} voice={voiceId}");
            }
        });

        link.RemoteVoiceRemoved += (Il2CppSystem.Action)(() =>
        {
            Main.DebugLog($"VoiceRemoved actor={playerId} voice={voiceId}");
            StopWriter(voiceId);
        });
    }

    public static void StopRecording()
    {
        isRecording = false;

        foreach (var key in writers.Keys.ToList())
            StopWriter(key);

        VoiceTrackInfos.Clear();
        Directory.Delete(TempVoiceDir);
    }


    private static void StopWriter(int voiceId)
    {
        if (!writers.Remove(voiceId, out var writer))
            return;

        writer.Dispose();

        if (!writer.HasFrames && File.Exists(writer.Path))
            File.Delete(writer.Path);
    }

    public class VoiceStreamWriter : IDisposable
    {
        public readonly int VoiceId;
        public readonly string Path;
        public bool HasFrames { get; private set; }
        public float LastWriteTimestamp { get; private set; }

        public int SampleRate;
        public int Channels;

        private readonly FileStream fileStream;
        private readonly IOpusEncoder encoder;
        private readonly OpusOggWriteStream wav;

        public VoiceStreamWriter(
            int voiceId,
            int sampleRate,
            int channels,
            string path,
            int bitrate = 30000
        )
        {
            VoiceId = voiceId;
            Path = path;

            fileStream = File.Create(path);

            VoiceId = voiceId;
            SampleRate = sampleRate;
            Channels = channels;

            encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = bitrate;
            encoder.UseVBR = true;

            OpusTags tags = new();
            tags.Comment = "Rumble ReplayMod captured photon voice stream";
            wav = new(encoder, fileStream, tags);
            LastWriteTimestamp = Time.time;
        }

        public void Write(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return;

            HasFrames = true;
            int silenceSamplesAmount = (int)((Time.time - LastWriteTimestamp) * SampleRate);
            if (silenceSamplesAmount > SampleRate / 8)
            {
                wav.WriteSamples(new float[silenceSamplesAmount], 0, silenceSamplesAmount);
                Main.DebugLog($"Silenced:{silenceSamplesAmount}");
            }
            LastWriteTimestamp = Time.time;
            wav.WriteSamples(samples, 0, samples.Length);
        }

        public void Dispose()
        {
            wav.Finish();
            fileStream?.Dispose();
        }
    }
}
