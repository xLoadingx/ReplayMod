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
using Concentus.Enums;
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

    private static readonly Dictionary<(int actorId, int voiceId), VoiceStreamWriter> writers = new();
    public static string VoiceCacheDir = Path.Combine(MelonEnvironment.UserDataDirectory, "ReplayMod", "VoiceCache");

    public static List<VoiceTrackInfo> VoiceTrackInfos = new();

    public static bool isRecording;
    private static bool subscribed;

    public static bool HasActiveWriters => writers.Count > 0;

    public static void StartRecording()
    {
        if (!Main.instance.VoiceRecording.Value)
            return;
        
        isRecording = true;
        
        VoiceTrackInfos.Clear();

        if (Directory.Exists(VoiceCacheDir))
        {
            foreach (var file in Directory.GetFiles(VoiceCacheDir))
                File.Delete(file);
        }
        else
        {
            Directory.CreateDirectory(VoiceCacheDir);
        }

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

            Directory.CreateDirectory(VoiceCacheDir);
        }
    }

    public static void StopRecording()
    {
        isRecording = false;

        foreach (var key in writers.Keys.ToList())
            stopWriter(key.actorId, key.voiceId);
    }

    public static AudioClip LoadVoiceClipFromFile(
        string path
    )
    {
        byte[] data = File.ReadAllBytes(path);

        float[] pcm = DecodeOpus(data, out int sampleRate, out int channels);

        var clip = AudioClip.Create(
            Path.GetFileNameWithoutExtension(path),
            pcm.Length / channels,
            channels,
            sampleRate,
            false
        );

        clip.hideFlags = HideFlags.DontUnloadUnusedAsset;

        clip.SetData(pcm, 0);
        return clip;
    }

    public static float[] DecodeOpus(byte[] data, out int sampleRate, out int channels)
    {
        using var ms = new MemoryStream(data);

        var decoder = OpusCodecFactory.CreateDecoder(48000, 1);
        var ogg = new OpusOggReadStream(decoder, ms);

        sampleRate = 48000;
        channels = 1;

        List<float> samples = new();

        while (ogg.HasNextPacket)
        {
            short[] pcm = ogg.DecodeNextPacket();

            if (pcm != null)
            {
                for (int i = 0; i < pcm.Length; i++)
                    samples.Add(pcm[i] / 32768f);
            }
        }

        return samples.ToArray();
    }

    public static void Cleanup()
    {
        VoiceTrackInfos.Clear();
        
        if (Directory.Exists(VoiceCacheDir))
            Directory.Delete(VoiceCacheDir, true);
    }
    
    private static void stopWriter(int actorId, int voiceId)
    {
        if (!writers.Remove((actorId, voiceId), out var writer))
            return;

        writer.Dispose();

        if (!writer.HasFrames && File.Exists(writer.Path))
            File.Delete(writer.Path);
    }

    public static void OnRemoteVoiceLinkAdded(RemoteVoiceLink link)
    {
        if (!isRecording)
            return;
        
        int playerId = link.PlayerId;
        int voiceId = link.VoiceId;

        float startTime = Time.time - Main.Recording.recordingStartTime;
        Main.DebugLog($"VoiceAdded actor={playerId} voice={voiceId}");

        var player = PlayerManager.instance.AllPlayers.ToArray()
            .FirstOrDefault(p => p.Data.GeneralData.ActorNo == playerId);

        string name = player?.Data.GeneralData.PublicUsername ?? "Unknown";
        string masterId = player?.Data.GeneralData.PlayFabMasterId;

        name = Utilities.CleanName(name);

        link.FloatFrameDecoded += (Il2CppSystem.Action<FrameOut<float>>)((FrameOut<float> frame) =>
        {
            if (!isRecording)
                return;

            if (!writers.TryGetValue((playerId, voiceId), out var writer))
            {
                string fileName = $"p{name}_{masterId}_v{voiceId}_{Time.frameCount}.ogg";
                string path = Path.Combine(VoiceCacheDir, fileName);

                writer = new VoiceStreamWriter(
                    voiceId,
                    link.VoiceInfo.SamplingRate,
                    link.VoiceInfo.Channels,
                    path,
                    bitrate: Mathf.Clamp(Main.instance.voiceBitrate.Value, 8, 128),
                    initalTimeStamp: startTime // file doesnt actually start until the person starts speaking for the first time
                );

                writers.Add((playerId, voiceId), writer);

                VoiceTrackInfos.Add(new VoiceTrackInfo(
                    masterId,
                    playerId,
                    voiceId,
                    fileName,
                    startTime
                ));
            }

            writer.Write(frame.Buf);
        });

        link.RemoteVoiceRemoved += (Il2CppSystem.Action)(() => stopWriter(playerId, voiceId));
    }

    public class VoiceStreamWriter : IDisposable
    {
        public readonly int VoiceId;
        public readonly string Path;
        public bool HasFrames { get; private set; }
        public float LastWriteTimestamp { get; private set; }

        public int SampleRate { get; private set; }
        public int Channels { get; private set; }

        private readonly FileStream _fileStream;
        private readonly IOpusEncoder _encoder;
        private readonly OpusOggWriteStream _wav;

        public VoiceStreamWriter(
            int voiceId,
            int sampleRate,
            int channels,
            string path,
            int bitrate = 30000,
            int complexity = 8,
            float initalTimeStamp = 0
        )
        {
            LastWriteTimestamp = Time.time;

            VoiceId = voiceId;
            Path = path;
            SampleRate = sampleRate;
            Channels = channels;

            _fileStream = File.Create(path);
            
            _encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = bitrate;
            _encoder.Complexity = complexity;
            _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

            OpusTags tags = new();
            tags.Fields.Add("Type", "Rumble ReplayMod Captured Voice");
            tags.Fields.Add("Origin", "https://github.com/xLoadingx/ReplayMod");
            tags.Fields.Add("Encoder", "https://github.com/lostromb/concentus");
            _wav = new(_encoder, _fileStream, tags);
        }

        public void Write(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return;

            HasFrames = true;
            int silenceSamplesAmount = (int)((Time.time - LastWriteTimestamp) * SampleRate);
            if (silenceSamplesAmount >= SampleRate / 8) // kinda arbitary silence amount
            {
                Main.DebugLog($"Voice {VoiceId} silent for {Time.time - LastWriteTimestamp} seconds or {silenceSamplesAmount} samples");
                _wav.WriteSamples(new float[silenceSamplesAmount], 0, silenceSamplesAmount);
            }
            _wav.WriteSamples(samples, 0, samples.Length);
            LastWriteTimestamp = Time.time;
        }

        public void Dispose()
        {
            _wav.Finish();
            _fileStream?.Dispose();
        }
    }
}
