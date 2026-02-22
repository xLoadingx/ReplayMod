using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ReplayMod.Core;
using ReplayMod.Replay.Files;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using RumbleModUIPlus;
using UnityEngine;
using BuildInfo = ReplayMod.Core.BuildInfo;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Tags = RumbleModUIPlus.Tags;

namespace ReplayMod.Replay;

public static class ReplayAPI
{
    private static readonly List<Extension> _extensions = new();
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
    public static event Action<Frame> OnPlaybackFrame;
    
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

    internal static void OnPlaybackFrameInternal(Frame frame) => OnPlaybackFrame?.Invoke(frame);
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
    
    internal static IEnumerable<Extension> Extensions => _extensions;

    /// <summary>
    /// Computes a stable FNV-1a hash for a string.
    /// Used to generate consistent frame extension identifiers for custom chunks.
    /// </summary>
    private static int ComputeStableId(string input)
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
    /// Attempts to retrieve a registered frame reader for the specified extension id.
    /// </summary>
    /// <param name="id">The stable frame extension id.</param>
    /// <param name="reader">The frame reader delegate if found</param>
    /// <returns>True if a reader was found.</returns>
    internal static bool TryGetFrameReader(int id, out Action<BinaryReader, Frame, int> reader)
    {
        var ext = _extensions.FirstOrDefault(e => e.FrameExtensionId == id);

        if (ext != null && ext.OnReadFrame != null)
        {
            reader = ext.OnReadFrame;
            return true;
        }

        reader = null;
        return false;
    }
    
    /// <summary>
    /// Registers a replay extension.
    /// Allows injecting custom archive data and per-frame data.
    /// The provided id must be unique.
    /// </summary>
    /// <param name="id">Unique identifier for the extension. This must be kept the same for the extension to be identified.</param>
    /// <param name="onBuild">Called when building the replay archive.</param>
    /// <param name="onRead">Called when reading the replay archive.</param>
    /// <param name="onWriteFrame">Called when writing each frame</param>
    /// <param name="onReadFrame">Called when reading each frame.</param>
    /// <returns>The replay extension class.</returns>
    public static ReplayExtension RegisterExtension(
        string id, 
        Action<ArchiveBuilder> onBuild = null, 
        Action<ArchiveReader> onRead = null,
        Action<FrameExtensionWriter, Frame> onWriteFrame = null,
        Action<BinaryReader, Frame, int> onReadFrame = null)
    {
        if (_extensions.Any(e => e.Id == id))
        {
            Main.instance.LoggerInstance.Error($"Replay Mod: Extension '{id}' already registered");
            return null;
        }
        
        _extensions.Add(new Extension(
            id, 
            onBuild, 
            onRead,
            ComputeStableId(id),
            onWriteFrame, 
            onReadFrame)
        );

        Main.instance.extensionsFolder ??= Main.replayMod.AddFolder("Extensions", "Settings for all registered extensions");

        var extensionFolder = Main.replayMod.AddFolder(id, $"Settings for the extension '{id}'");
        Main.instance.extensionsFolder.AddSetting(extensionFolder);

        var extensionToggle = Main.replayMod.AddToList("Toggle", true, 0, "Toggles the extension on/off", new Tags());
        extensionFolder.AddSetting(extensionToggle);

        Main.replayMod.GetFromFile();
        
        Main.instance.LoggerInstance.Msg($"Replay Mod: Extension '{id}' created");

        return new ReplayExtension(id);
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
            ext.OnBuild?.Invoke(builder);
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
            ext.OnRead?.Invoke(reader);
        }
    }

    /// <summary>
    /// Represents a registered replay extension definition.
    /// Stores callbacks and metadata used during replay serialization.
    /// </summary>
    internal sealed class Extension
    {
        public string Id { get; }
        
        public Action<ArchiveBuilder> OnBuild { get; }
        public Action<ArchiveReader> OnRead { get; }

        public int FrameExtensionId { get; }
        
        public Action<FrameExtensionWriter, Frame> OnWriteFrame { get; }
        public Action<BinaryReader, Frame, int> OnReadFrame { get; }

        public Extension(
            string id, 
            Action<ArchiveBuilder> onBuild, 
            Action<ArchiveReader> onRead,
            int frameExtensionId,
            Action<FrameExtensionWriter, Frame> onWriteFrame,
            Action<BinaryReader, Frame, int> onReadFrame)
        {
            Id = id;
            OnBuild = onBuild;
            OnRead = onRead;
            FrameExtensionId = frameExtensionId;
            OnWriteFrame = onWriteFrame;
            OnReadFrame = onReadFrame;
        }
    }

    /// <summary>
    /// Represents a registered replay extension.
    /// </summary>
    public sealed class ReplayExtension
    {
        private readonly string _modId;

        internal ReplayExtension(string modId)
        {
            _modId = modId;
        }

        /// <summary>
        /// Adds a marker to the current recording.
        /// Returns false if recording/buffering is not active.
        /// </summary>
        /// <param name="name">The name of the marker. Can be anything.</param>
        /// <param name="time">The timestamp at which the marker is added. Use Time.time to add a marker at the current frame.</param>
        /// <param name="color">The color in which the marker appears on the timeline.</param>
        /// <returns>The added marker</returns>
        public Marker AddMarker(string name, float time, Color color) =>
            Main.Recording.AddMarker($"{_modId}.{name}", color, time);
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