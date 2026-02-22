using System.Collections.Generic;
using System.IO;
using MelonLoader;
using ReplayMod;
using ReplayMod.Replay;
using ReplayMod.Replay.Serialization;
using RumbleModdingAPI;
using UnityEngine;

namespace ReplayMod.docs.Extensions;

public class ExampleMod : MelonMod
{
    // This example demonstrates how to extend replays by recording
    // and replaying a scene object (in this case, the Park bell).
    
    // Stores the bell state captured during recording.
    private static readonly Dictionary<Frame, BellState> recordedBellFrames = new();
    
    // Stores reconstructed state during playback.
    private static readonly Dictionary<Frame, BellState> reconstructedBellFrames = new();

    // Used when reading frames to allow state to carry forward from delta-compression
    // Delta-compression is not used in this example, but is highly recommended.
    private static BellState lastState;

    private ReplayAPI.ReplayExtension mod;
    
    // The bell only exists in the "Park" scene
    private string currentScene = "Loader";

    public override void OnLateInitializeMelon()
    {
        // The ID must remain the same or previously saved Replays
        // will no longer associate with this extension.
        mod = ReplayAPI.RegisterExtension(
            id: "BellSupport",
            onWriteFrame: OnWriteFrame,
            onReadFrame: OnReadFrame
        );

        // Called every frame during recording/buffering
        ReplayAPI.OnRecordFrame += OnRecordFrame;
        
        // Called every frame during playback
        ReplayAPI.OnPlaybackFrame += OnPlaybackFrame;

        ReplayAPI.onReplayEnded += _ =>
        {
            recordedBellFrames.Clear();
            reconstructedBellFrames.Clear();
            lastState = null;
        };
    }

    private void OnRecordFrame(Frame frame, bool isBuffer)
    {
        if (currentScene != "Park")
            return;

        var bell = Calls.GameObjects.Park.LOGIC.Interactables.Bell.GetGameObject();
        if (bell == null)
            return;
        
        // Capture transform state for this frame
        recordedBellFrames[frame] = new BellState
        {
            Position = bell.transform.position,
            Rotation = bell.transform.rotation
        };
    }

    private void OnWriteFrame(ReplayAPI.FrameExtensionWriter writer, Frame frame)
    {
        // If this frame has no recorded data, write nothing.
        if (!recordedBellFrames.TryGetValue(frame, out var state))
            return;
        
        /*
         * Each Write(field, value) call writes:
         *   - Field ID (1 byte)
         *   - Field payload length (1 byte)
         *   - The actual data (N bytes)
         *
         * The mod groups these field entries into a single chunk
         * for this extension automatically.
         *
         * IMPORTANT:
         *   - Only write fields that changed between frames (delta encoding recommended).
         *   - Do NOT manually write field IDs or lengths using raw bw.Write.
         *     Always use the provided BinaryWriter.Write(field, value) overloads.
         *
         * Field payloads are limited to 255 bytes.
         * If you somehow hit that limit for a single field,
         * split your data into more than 1 field!
        */
        
        writer.WriteChunk(0, w =>
        {
            w.Write(BellField.Position, state.Position);
            w.Write(BellField.Rotation, state.Rotation);
        });
    }

    private void OnReadFrame(BinaryReader br, Frame frame, int subIndex)
    {
        /*
         * ReadChunk builds a state object for this frame.
         *
         * The ctor function used to create the initial state for this frame.
         * Each field encountered in the chunk mutates the state via the callback.
         *
         * When finished, ReadChunk returns the fully reconstructed  state.
         *
         * Unknown fields are automatically skipped.
         *
         * Technically, the ctor function here is unnecessary due to our lack of delta-compression,
         * but it is highly recommended to do so.
         */
        var state = ReplaySerializer.ReadChunk<BellState, BellField>(
            br,
            () => lastState?.Clone() ?? new BellState(),
            (s, field, size, reader) =>
            {
                switch (field)
                {
                    case BellField.Position:
                        s.Position = reader.ReadVector3();
                        break;

                    case BellField.Rotation:
                        s.Rotation = reader.ReadQuaternion();
                        break;
                }
            });

        reconstructedBellFrames[frame] = state;
    }

    private void OnPlaybackFrame(Frame frame)
    {
        if (currentScene != "Park")
            return;
        
        if (!reconstructedBellFrames.TryGetValue(frame, out var state))
            return;
        
        var bell = Calls.GameObjects.Park.LOGIC.Interactables.Bell.GetGameObject();
        if (bell == null)
            return;

        // Apply reconstructed transform state to the live object.
        bell.transform.position = state.Position;
        bell.transform.rotation = state.Rotation;
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        currentScene = sceneName;
    }

    // Field identifiers used when writing frame data.
    // These values are serialized as byte tags and must remain in a stable order.
    private enum BellField : byte
    {
        Position,
        Rotation
    }

    // Simple container for bell transform state
    private class BellState
    {
        public Vector3 Position;
        public Quaternion Rotation;

        // Used to preserve previous state during reconstruction.
        public BellState Clone()
        {
            return new BellState
            {
                Position = Position,
                Rotation = Rotation
            };
        }
    }
}