using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ReplayMod.Core;
using ReplayMod.Replay.Files;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using RumbleModUI;
using RumbleModUIPlus;
using UnityEngine;
using BuildInfo = ReplayMod.Core.BuildInfo;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Mod = RumbleModUI.Mod;

namespace ReplayMod.Replay;

public static class ReplayAPI
{
    private static readonly List<ReplayExtension> _extensions = new();
    private static readonly List<ModSettingFolder> _extensionFolders = new(); 
    
    /// <summary>
    /// Invoked when a replay is selected from the UI. If header / path is null, then there is no replay selected.
    /// </summary>
    public static event Action<ReplaySerializer.ReplayHeader, string> onReplaySelected;
    
    /// <summary>
    /// Invoked when playback begins for a replay.
    /// Everything for the replay is loaded at this point.
    /// </summary>
    public static event Action<ReplayInfo> onReplayStarted;
    
    /// <summary>
    /// Invoked when playback is stopped and all replay objects are destroyed.
    /// </summary>
    public static event Action<ReplayInfo> onReplayEnded;
    
    /// <summary>
    /// Invoked when the playback time changes (seek or progression).
    /// </summary>
    public static event Action<float> onReplayTimeChanged;
    
    /// <summary>
    /// Invoked when playback is paused or resumed, along with the new toggle state.
    /// </summary>
    public static event Action<bool> onReplayPauseChanged;

    /// <summary>
    /// Invoked for every frame during playback.
    /// </summary>
    public static event Action<Frame, Frame> OnPlaybackFrame;
    
    /// <summary>
    /// Invoked for every frame while recording or buffering.
    /// The boolean indicates whether the frame belongs to the buffer.
    /// </summary>
    public static event Action<Frame, bool> OnRecordFrame;

    
    /// <summary>
    /// Invoked after a replay is saved to disk.
    /// </summary>
    public static event Action<ReplayInfo, bool, string> onReplaySaved;
    
    /// <summary>
    /// Invoked after a replay file is deleted, along with its path.
    /// </summary>
    public static event Action<string> onReplayDeleted;
    
    /// <summary>
    /// Invoked after a replay is renamed, along with its new path.
    /// </summary>
    public static event Action<ReplaySerializer.ReplayHeader, string> onReplayRenamed;

    internal static void ReplaySelectedInternal(ReplaySerializer.ReplayHeader info, string path) => onReplaySelected?.Invoke(info, path);
    internal static void ReplayStartedInternal(ReplayInfo info) => onReplayStarted?.Invoke(info);
    internal static void ReplayEndedInternal(ReplayInfo info) => onReplayEnded?.Invoke(info);
    internal static void ReplayTimeChangedInternal(float time) => onReplayTimeChanged?.Invoke(time);
    internal static void ReplayPauseChangedInternal(bool paused) => onReplayPauseChanged?.Invoke(paused);

    internal static void OnPlaybackFrameInternal(Frame frame, Frame nextFrame) => OnPlaybackFrame?.Invoke(frame, nextFrame);
    internal static void OnRecordFrameInternal(Frame frame, bool isBuffer) => OnRecordFrame?.Invoke(frame, isBuffer);
    
    internal static void ReplaySavedInternal(ReplayInfo info, bool isBuffer, string path) => onReplaySaved?.Invoke(info, isBuffer, path);
    internal static void ReplayDeletedInternal(string path) => onReplayDeleted?.Invoke(path);
    internal static void ReplayRenamedInternal(ReplaySerializer.ReplayHeader header, string newPath) => onReplayRenamed?.Invoke(header, newPath);

    /// <summary>
    /// Gets whether a recording is currently active.
    /// This is enabled when a user manually starts recording.
    /// </summary>
    public static bool IsRecording => Main.Recording.isRecording;
    
    /// <summary>
    /// Gets whether the buffer is currently recording frames.
    /// Separate from manual recording.
    /// </summary>
    public static bool IsBuffering => Main.Recording.isBuffering;
    
    /// <summary>
    /// Gets whether playback is currently active.
    /// </summary>
    public static bool IsPlaying => Main.Playback.isPlaying;
    
    /// <summary>
    /// Gets whether playback is currently paused.
    /// </summary>
    public static bool IsPaused => Main.Playback.isPaused;
    
    /// <summary>
    /// The elapsed time (in seconds) of playback.
    /// </summary>
    public static float CurrentTime => Main.Playback.elapsedPlaybackTime;
    
    /// <summary>
    /// The total duration of playback.
    /// </summary>
    public static float Duration => Main.Playback.currentReplay?.Header?.Duration ?? 0f;

    /// <summary>
    /// Returns the current format version that replays are written in.
    /// </summary>
    public static Version FormatVersion => new (BuildInfo.FormatVersion);

    /// <summary>
    /// Returns the ModUI reference of the mod.
    /// </summary>
    public static Mod ReplayMod => Main.replayMod;
    
    /// <summary>
    /// Gets the list of all player clones in the active playback.
    /// </summary>
    public static IReadOnlyList<ReplayPlayback.Clone> Players => Main.Playback.PlaybackPlayers;
    
    /// <summary>
    /// Gets the list of all playback structures in the active playback.
    /// </summary>
    public static IReadOnlyList<GameObject> Structures => Main.Playback.PlaybackStructures;
    
    /// <summary>
    /// Gets the currently loaded replay, if any.
    /// </summary>
    public static ReplayInfo CurrentReplay => Main.Playback.currentReplay;

    /// <summary>
    /// Gets the template for the input scene's metadata
    /// </summary>
    /// <returns>The untagged metadata template for the input scene</returns>
    public static string GetMetadataFormat(string sceneName) => ReplayFiles.GetMetadataFormat(sceneName);

    /// <summary>
    /// Gets the filled-in version of a template from the provided replay header (as shown on the Replay Table)
    /// </summary>
    /// <param name="template">The untagged template string</param>
    /// <param name="replayInfo">The info to fill in the template with</param>
    /// <returns>Formatted string for the input replay</returns>
    public static string FormatReplayTemplate(string template, ReplaySerializer.ReplayHeader replayInfo) => 
        ReplayFormatting.FormatReplayString(template, replayInfo);

    /// <summary>
    /// Gets the displayed name for a replay (as shown on the Replay Table) using the provided path and info.
    /// </summary>
    /// <param name="replayPath">The path to the input replay</param>
    /// <param name="replayInfo">The header for the input replay</param>
    /// <param name="alternativeName">Alternate name to use instead of the file name</param>
    /// <param name="displayTitle">Whether to show the title if the file name starts with 'Replay'</param>
    /// <returns></returns>
    public static string GetReplayDisplayName(string replayPath, ReplaySerializer.ReplayHeader replayInfo, string alternativeName = null, bool displayTitle = true) =>
        ReplayFormatting.GetReplayDisplayName(replayPath, replayInfo, alternativeName, displayTitle);
    
    /// <summary>
    /// Loads and begins playback of the replay at the specified file path.
    /// This does not change scenes.
    /// </summary>
    /// <param name="path">The path to the replay</param>
    public static void Play(string path) => Main.Playback.LoadReplay(path);

    /// <summary>
    /// Loads and begins playback of the currently selected replay on the Replay Table.
    /// This loads everything necessary for the replay to look correct.
    /// <see cref="onReplayStarted"/> is called after loading is finished.
    /// </summary>
    public static void LoadSelectedReplay() => 
        Main.instance.LoadSelectedReplay();
    
    /// <summary>
    /// Stops and gets rid of the current replay and its objects.
    /// </summary>
    public static void Stop() => Main.Playback.StopReplay();

    /// <summary>
    /// Starts a new manual recording session.
    /// </summary>
    public static void StartRecording() => Main.Recording.StartRecording();
    
    /// <summary>
    /// Stops and saves the current recording session to a replay.
    /// </summary>
    public static void StopRecording() => Main.Recording.StopRecording();

    /// <summary>
    /// Starts buffering with the user-specified buffer length.
    /// </summary>
    public static void StartBuffering() => Main.Recording.StartBuffering();
    
    /// <summary>
    /// Saves the current buffer to a replay file.
    /// </summary>
    public static void SaveBuffer() => Main.Recording.SaveReplayBuffer();
    
    /// <summary>
    /// Pauses or resumes playback.
    /// </summary>
    /// <param name="playing">Whether the playback is playing or not</param>
    public static void TogglePlayback(bool playing) => Main.Playback.TogglePlayback(playing);
    
    /// <summary>
    /// Seeks playback to the specified time in seconds.
    /// </summary>
    /// <param name="time">Target time (in seconds)</param>
    public static void Seek(float time) => Main.Playback.SetPlaybackTime(time);
    
    /// <summary>
    /// Seeks playback to the specified frame index.
    /// </summary>
    /// <param name="frame">Target frame index</param>
    public static void Seek(int frame) => Main.Playback.SetPlaybackFrame(frame);
    
    /// <summary>
    /// Sets the playback speed multiplier.
    /// </summary>
    /// <param name="speed">Target speed</param>
    public static void SetSpeed(float speed) => Main.Playback.SetPlaybackSpeed(speed);

    private static readonly Dictionary<int, Action<BinaryReader, Frame>> _frameReaders = new();
    private static readonly Dictionary<int, Action<FrameExtensionWriter, Frame>> _frameWriters = new();
    
    internal static IEnumerable<ReplayExtension> Extensions => _extensions;

    /// <summary>
    /// Computes a stable FNV-1a hash for a string.
    /// Used to generate consistent frame extension identifiers for custom chunks.
    /// </summary>
    public static int ComputeStableId(string input)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;

            foreach (char c in input)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return hash;
        }
    }
    
    /// <summary>
    /// Registers a replay extension.
    /// Allows injecting custom archive data and per-frame data.
    /// The provided id must be unique.
    /// </summary>
    /// <param name="extension">The replay extension class</param>
    /// <returns>The replay extension class</returns>
    public static ReplayExtension RegisterExtension(ReplayExtension extension)
    {
        if (_extensions.Any(e => e.Id == extension.Id))
        {
            Main.instance.LoggerInstance.Error($"Extension with an ID `{extension.Id}` already registered");
            return null;
        }
        
        _extensions.Add(extension);
        
        Main.replayMod.GetFromFile();
        Main.instance.LoggerInstance.Msg($"Extension '{extension.Id}' created");

        return extension;
    }

    /// <summary>
    /// Invokes all registered archive build callbacks.
    /// Called when constructing a replay archive.
    /// </summary>
    internal static void InvokeArchiveBuild(ZipArchive zip)
    {
        foreach (var ext in Extensions)
        {
            var builder = new ArchiveBuilder(zip, ext.Id);
            ext.OnBuild(builder);
        }
    }
    
    /// <summary>
    /// Invokes all registered archive read callbacks.
    /// Called when loading a replay archive.
    /// </summary>
    internal static void InvokeArchiveRead(ZipArchive zip)
    {
        foreach (var ext in Extensions)
        {
            var reader = new ArchiveReader(zip, ext.Id);
            ext.OnRead(reader);
        }
    }

    /// <summary>
    /// Provides structured writing access for replay frame extensions.
    /// Allows an extension to emit one or more sub-chunks within a single frame.
    /// </summary>
    /// <remarks>
    ///Each call to <see cref="WriteChunk"/> produces a distinct
    /// <see cref="ChunkType.Extension"/> entry in the frame stream,
    /// associated with the owning extension's identifier.
    /// </remarks>
    public sealed class FrameExtensionWriter
    {
        private readonly BinaryWriter _entriesWriter;
        private readonly int _extensionId;
        private readonly Action _incrementEntryCount;

        internal FrameExtensionWriter(
            BinaryWriter entriesWriter,
            int extensionId,
            Action incrementEntryCount
        )
        {
            _entriesWriter = entriesWriter;
            _extensionId = extensionId;
            _incrementEntryCount = incrementEntryCount;
        }

        /// <summary>
        /// Writes a single extension sub-chunk to the current frame.
        /// </summary>
        /// <param name="subIndex">
        /// An extension-defined indentifier used to distinguish separate entities
        /// (for example: player index, object index, or custon ID).
        /// </param>
        /// <param name="write">
        /// Callback used to serialize the chunk payload using a temporary <see cref="BinaryWriter"/>
        /// </param>
        public void WriteChunk(int subIndex, Action<BinaryWriter> write)
        {
            using var chunkMs = new MemoryStream();
            using var bw = new BinaryWriter(chunkMs);

            write(bw);

            if (chunkMs.Length == 0)
                return;

            _entriesWriter.Write((byte)ChunkType.Extension);
            _entriesWriter.Write(_extensionId);
            _entriesWriter.Write(subIndex);
            _entriesWriter.Write((int)chunkMs.Length);
            _entriesWriter.Write(chunkMs.ToArray());

            _incrementEntryCount?.Invoke();
        }
    }
    
    /// <summary>
    /// Provides utilities for writing extension-specific files into a replay archive during build.
    /// </summary>
    public sealed class ArchiveBuilder
    {
        private readonly ZipArchive _zip;
        private readonly string _modId;

        internal ArchiveBuilder(ZipArchive zip, string modId)
        {
            _zip = zip;
            _modId = modId;
        }

        /// <summary>
        /// Adds a file to the replay archive under the extension's namespace.
        /// </summary>
        /// <param name="path">Relative path within the extension folder.</param>
        /// <param name="data">Raw file contents.</param>
        /// <param name="level">Compression level to use</param>
        public void AddFile(string path, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
        {
            var entry = _zip.CreateEntry($"extensions/{_modId}/{path}", level);

            using var stream = entry.Open();
            stream.Write(data, 0, data.Length);
        }
    }
    
    /// <summary>
    /// Provides utilities for reading extension-specific files from a replay archive.
    /// </summary>
    public sealed class ArchiveReader
    {
        private readonly ZipArchive _zip;
        private readonly string _modId;

        internal ArchiveReader(ZipArchive zip, string modId)
        {
            _zip = zip;
            _modId = modId;
        }

        private string GetFullPath(string relativePath) => $"extensions/{_modId}/{relativePath}";

        /// <summary>
        /// Attempts to read a file from the extension's acrhive namespace.
        /// </summary>
        /// <param name="relativePath">Relative path within the extension folder.</param>
        /// <param name="data">The file data if found</param>
        /// <returns>True if the file exists</returns>
        public bool TryGetFile(string relativePath, out byte[] data)
        {
            var entry = _zip.GetEntry(GetFullPath(relativePath));

            if (entry == null)
            {
                data = null;
                return false;
            }

            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            data = ms.ToArray();
            return true;
        }

        /// <summary>
        /// Checks whether a file exists within the extension's archive namespace.
        /// </summary>
        public bool FileExists(string relativePath) => _zip.GetEntry(GetFullPath(relativePath)) != null;
    }
}

/// <summary>
/// Base class for replay extensions.
///
/// Extensions can work with the way replays are serialized by overriding
/// the provided virtual methods.
/// </summary>
public abstract class ReplayExtension
{
    /// <summary>
    /// A stable, unique identifier for this extension.
    /// Chaging this value will break compatibility with
    /// previously recorded replays using this extension.
    /// </summary>
    public abstract string Id { get; }
    
    /// <summary>
    /// The settings folder for this extension.
    /// Additional settings can be added to this folder after registration.
    /// </summary>
    public ModSettingFolder Settings { get; internal set; }
    
    /// <summary>
    /// The enable toggle controlling whether this extension
    /// is allowed to serialize its data.
    /// </summary>
    public ModSetting<bool> Enabled { get; internal set; }
    
    /// <summary>
    /// Gets whether this extension is currently enabled.
    /// </summary>
    public bool IsEnabled => (bool)Enabled.SavedValue;

    /// <summary>
    /// Called when building the replay archive.
    /// Allows the extension to write data into the archive.
    /// </summary>
    public virtual void OnBuild(ReplayAPI.ArchiveBuilder builder) { }
    
    /// <summary>
    /// Called when reading the replay archive.
    /// Allows the extension to read data from the archive
    /// written during <see cref="OnBuild"/>.
    /// </summary>
    public virtual void OnRead(ReplayAPI.ArchiveReader reader) { }

    /// <summary>
    /// Gets the stable numeric identifier derived from <see cref="Id"/>.
    /// Used internally to associate frame data with this extension.
    /// </summary>
    public int FrameExtensionId => ReplayAPI.ComputeStableId(Id);

    /// <summary>
    /// Called during recording for each captured frame.
    /// Extensions should capture live state here and attach it
    /// to the tprovided <see cref="Frame"/> using
    /// <c>Frame.SetExtensionData(...)</c>.
    /// </summary>
    /// <param name="frame">The frame currently being recorded.</param>
    /// <param name="isBuffer">True if this frame is part of the rolling buffer.</param>
    public virtual void OnRecordFrame(Frame frame, bool isBuffer) { }
    
    /// <summary>
    /// Called when serializing a recorded frame.
    /// Extensions should write previously recorded frame data
    /// using the provided <see cref="ReplayAPI.FrameExtensionWriter"/>.
    /// </summary>
    /// <param name="writer">The writer for this frame's extension chunk.</param>
    /// <param name="frame">The frame being serialized.</param>
    public virtual void OnWriteFrame(ReplayAPI.FrameExtensionWriter writer, Frame frame) { }
    
    /// <summary>
    /// Called when deserializing a frame extension chunk.
    /// Extensions should reconstruct state and attach it to the
    /// provided <see cref="Frame"/> using
    /// <c>Frame.SetExtensionData(...)</c>.
    /// </summary>
    /// <param name="reader">Binary reader positioned at the extension payload.</param>
    /// <param name="frame">The frame being reconstructed.</param>
    /// <param name="index">The previously written index of this extension chunk. Can be ignored if unused.</param>
    public virtual void OnReadFrame(BinaryReader reader, Frame frame, int index) { }
    
    /// <summary>
    /// Called during playback for each frame.
    /// Extensions should retrieve previously reconstructed state
    /// from the frame and apply it to the live scene.
    /// </summary>
    /// <param name="frame">The current frame being played back.</param>
    /// <param name="nextFrame">The next frame in playback order. May be null if this is the last/first frame.</param>
    public virtual void OnPlaybackFrame(Frame frame, Frame nextFrame) { }
}