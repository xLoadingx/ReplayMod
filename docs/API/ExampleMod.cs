using System.IO;
using MelonLoader;
using ReplayMod.Replay;
using ReplayMod.Replay.Serialization;
using RumbleModdingAPI;
using RumbleModUI;
using UnityEngine;
using Main = ReplayMod.Core.Main;

namespace ReplayMod.docs.Extensions;

public class ExampleMod : MelonMod
{
    // This example demonstrates how to extend replays by recording
    // and replaying a scene object (in this case, the Park bell [RIP]).

    public static ExampleMod instance;
    public ExampleMod() => instance = this;

    // Used when reading frames to allow state to carry forward from delta-compression
    // Delta-compression is not used in this example, but is highly recommended.
    private static BellState lastState;

    private ReplayExtension mod;

    private static ModSetting<bool> recordBell;

    public string currentScene = "Loader";

    public override void OnLateInitializeMelon()
    {
        // The ID must remain the same or previously saved Replays
        // will no longer associate with this extension.
        mod = ReplayAPI.RegisterExtension(new BellExtension());

        // Extensions can have their own settings.
        recordBell = Main.replayMod.AddToList("Record Bell", true, 0, "Toggles whether the bell is recorded.", new Tags());
        mod.Settings.AddSetting(recordBell);

        ReplayAPI.onReplayEnded += _ => {
            lastState = null;
        };
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

    private class BellExtension : ReplayExtension
    {
        public override string Id => "BellSupport";
        
        public override void OnRecordFrame(Frame frame, bool isBuffer)
        {
            if (instance.currentScene != "Park")
                return;

            if (!(bool)recordBell.SavedValue)
                return;

            var bell = Calls.GameObjects.Park.LOGIC.Interactables.Bell.GetGameObject();
            if (bell == null)
                return;
            
            // Capture transform state for this frame
            frame.SetExtensionData(this, new BellState
            {
                Position = bell.transform.position,
                Rotation = bell.transform.rotation
            });
        }

        public override void OnWriteFrame(ReplayAPI.FrameExtensionWriter writer, Frame frame)
        {
            // If this frame has no recorded data, write nothing.
            if (!frame.TryGetExtensionData(this, out BellState state))
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
            */

            writer.WriteChunk(0, w =>
            {
                w.Write(BellField.Position, state.Position);
                w.Write(BellField.Rotation, state.Rotation);
            });
        }

        public override void OnReadFrame(BinaryReader br, Frame frame, int subIndex)
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

            frame.SetExtensionData(this, state);
            lastState = state;
        }

        // NextFrame should be used for interpolation, though that isn't implemented here.
        public override void OnPlaybackFrame(Frame frame, Frame nextFrame)
        {
            if (instance.currentScene != "Park")
                return;
            
            if (!frame.TryGetExtensionData(this, out BellState state))
                return;
            
            var bell = Calls.GameObjects.Park.LOGIC.Interactables.Bell.GetGameObject();
            if (bell == null)
                return;

            // Apply reconstructed transform state to the live object.
            bell.transform.position = state.Position;
            bell.transform.rotation = state.Rotation;
        }
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