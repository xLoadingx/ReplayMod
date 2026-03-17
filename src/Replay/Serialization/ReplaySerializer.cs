using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Newtonsoft.Json;
using UnityEngine;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;
using Main = ReplayMod.Core.Main;
using MemoryStream = System.IO.MemoryStream;

namespace ReplayMod.Replay.Serialization;

public class ReplaySerializer
{
    public static string FileName { get; set; }

    const float EPS = 0.00005f;
    const float ROT_EPS_ANGLE = 0.05f;

    [Serializable]
    public class ReplayHeader
    {
        public string Title;
        public string CustomMap;
        public string Version;
        public string Scene;
        public string Date;

        public float Duration;

        public int FrameCount;
        public int PedestalCount;
        public int ScenePropCount;
        public int MarkerCount;
        public int AvgPing;
        public int MaxPing;
        public int MinPing;
        public int TargetFPS;

        public bool isFavorited;

        public PlayerInfo[] Players;
        public StructureInfo[] Structures;
        public ScenePropInfo[] SceneProps;
        public Marker[] Markers;

        public string Guid;
    }
    
    // ----- Helpers -----

    static bool PosChanged(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude > EPS * EPS;
    }

    static bool RotChanged(Quaternion a, Quaternion b)
    {
        return Quaternion.Angle(a, b) > ROT_EPS_ANGLE;
    }

    static bool FloatChanged(float a, float b)
    {
        return Mathf.Abs(a - b) > EPS;
    }

    // ----- Field-based diffs -----
    
    static bool WriteIf(bool condition, Action write)
    {
        if (!condition)
            return false;

        write();
        return true;
    }
    
    static int WriteStructureDiff(BinaryWriter w, StructureState prev, StructureState curr)
    {
        long start = w.BaseStream.Position;

        WriteIf(
            PosChanged(prev.position, curr.position),
            () => w.Write(StructureField.position, curr.position)
        );

         WriteIf(
            RotChanged(prev.rotation, curr.rotation),
            () => w.Write(StructureField.rotation, curr.rotation)
        );

        WriteIf(
            prev.active != curr.active,
            () => w.Write(StructureField.active, curr.active)
        );

        WriteIf(
            prev.isLeftHeld != curr.isLeftHeld,
            () => w.Write(StructureField.isLeftHeld, curr.isLeftHeld)
        );
        
        WriteIf(
            prev.isRightHeld != curr.isRightHeld,
            () => w.Write(StructureField.isRightHeld, curr.isRightHeld)
        );

        WriteIf(
            prev.isFlicked != curr.isFlicked,
            () => w.Write(StructureField.isFlicked, curr.isFlicked)
        );

        WriteIf(
            prev.currentState != curr.currentState,
            () => w.Write(StructureField.currentState, (byte)curr.currentState)
        );
        
        WriteIf(
            prev.isTargetDisk != curr.isTargetDisk,
            () => w.Write(StructureField.isTargetDisk, curr.isTargetDisk)
        );

        return (int)(w.BaseStream.Position - start);
    }
    
    static int WritePlayerDiff(BinaryWriter w, PlayerState prev, PlayerState curr)
    {
        long start = w.BaseStream.Position;

        WriteIf(
            PosChanged(prev.VRRigPos, curr.VRRigPos),
            () => w.Write(PlayerField.VRRigPos, curr.VRRigPos)
        );

        WriteIf(
            RotChanged(prev.VRRigRot, curr.VRRigRot),
            () => w.Write(PlayerField.VRRigRot, curr.VRRigRot)
        );

        WriteIf(
            PosChanged(prev.LHandPos, curr.LHandPos),
            () => w.Write(PlayerField.LHandPos, curr.LHandPos)
        );

        WriteIf(
            RotChanged(prev.LHandRot, curr.LHandRot),
            () => w.Write(PlayerField.LHandRot, curr.LHandRot)
        );

        WriteIf(
            PosChanged(prev.RHandPos, curr.RHandPos),
            () => w.Write(PlayerField.RHandPos, curr.RHandPos)
        );

        WriteIf(
            RotChanged(prev.RHandRot, curr.RHandRot),
            () => w.Write(PlayerField.RHandRot, curr.RHandRot)
        );

        WriteIf(
            PosChanged(prev.HeadPos, curr.HeadPos),
            () => w.Write(PlayerField.HeadPos, curr.HeadPos)
        );

        WriteIf(
            RotChanged(prev.HeadRot, curr.HeadRot),
            () => w.Write(PlayerField.HeadRot, curr.HeadRot)
        );

        WriteIf(
            prev.currentStack != curr.currentStack,
            () => w.Write(PlayerField.currentStack, curr.currentStack)
        );

        WriteIf(
            prev.Health != curr.Health,
            () => w.Write(PlayerField.Health, curr.Health)
        );

        WriteIf(
            prev.active != curr.active,
            () => w.Write(PlayerField.active, curr.active)
        );

        WriteIf(
            prev.activeShiftstoneVFX != curr.activeShiftstoneVFX,
            () => w.Write(PlayerField.activeShiftstoneVFX, (byte)curr.activeShiftstoneVFX)
        );

        WriteIf(
            prev.leftShiftstone != curr.leftShiftstone,
            () => w.Write(PlayerField.leftShiftstone, (byte)curr.leftShiftstone)
        );
        
        WriteIf(
            prev.rightShiftstone != curr.rightShiftstone,
            () => w.Write(PlayerField.rightShiftstone, (byte)curr.rightShiftstone)
        );

        WriteIf(
            FloatChanged(prev.lgripInput, curr.lgripInput),
            () => w.Write(PlayerField.lgripInput, curr.lgripInput)
        );
        
        WriteIf(
            FloatChanged(prev.lthumbInput, curr.lthumbInput),
            () => w.Write(PlayerField.lthumbInput, curr.lthumbInput)
        );
        
        WriteIf(
            FloatChanged(prev.lindexInput, curr.lindexInput),
            () => w.Write(PlayerField.lindexInput, curr.lindexInput)
        );
        
        WriteIf(
            FloatChanged(prev.rgripInput, curr.rgripInput),
            () => w.Write(PlayerField.rgripInput, curr.rgripInput)
        );
        
        WriteIf(
            FloatChanged(prev.rthumbInput, curr.rthumbInput),
            () => w.Write(PlayerField.rthumbInput, curr.rthumbInput)
        );
        
        WriteIf(
            FloatChanged(prev.rindexInput, curr.rindexInput),
            () => w.Write(PlayerField.rindexInput, curr.rindexInput)
        );

        WriteIf(
            prev.rockCamActive != curr.rockCamActive,
            () => w.Write(PlayerField.rockCamActive, curr.rockCamActive)
        );

        WriteIf(
            PosChanged(prev.rockCamPos, curr.rockCamPos),
            () => w.Write(PlayerField.rockCamPos, curr.rockCamPos)
        );

        WriteIf(
            RotChanged(prev.rockCamRot, curr.rockCamRot),
            () => w.Write(PlayerField.rockCamRot, curr.rockCamRot)
        );
        
        WriteIf(
            FloatChanged(prev.ArmSpan, curr.ArmSpan),
            () => w.Write(PlayerField.armSpan, curr.ArmSpan)
        );
        
        WriteIf(
            FloatChanged(prev.Length, curr.Length),
            () => w.Write(PlayerField.length, curr.Length)
        );

        WriteIf(
            !string.IsNullOrEmpty(curr.visualData) && !string.Equals(prev.visualData, curr.visualData),
            () => w.Write(PlayerField.visualData, curr.visualData)
        );

        return (int)(w.BaseStream.Position - start);
    }

    static int WritePedestalDiff(BinaryWriter w, PedestalState prev, PedestalState curr)
    {
        long start = w.BaseStream.Position; 
            
        WriteIf(
            PosChanged(prev.position, curr.position),
            () => w.Write(PedestalField.position, curr.position)
        );

        WriteIf(
            prev.active != curr.active,
            () => w.Write(PedestalField.active, curr.active)
        );

        return (int)(w.BaseStream.Position - start);
    }

    static int WriteScenePropDiff(BinaryWriter w, ScenePropState prev, ScenePropState curr)
    {
        long start = w.BaseStream.Position;

        WriteIf(
            PosChanged(prev.position, curr.position),
            () => w.Write(ScenePropField.position, curr.position)
        );

        WriteIf(
            RotChanged(prev.rotation, curr.rotation),
            () => w.Write(ScenePropField.rotation, curr.rotation)
        );
        
        return (int)(w.BaseStream.Position - start);
    }

    static int WriteEvent(BinaryWriter w, EventChunk e)
    {
        long start = w.BaseStream.Position;
        
        WriteIf(
            true,
            () => w.Write(EventField.type, (byte)e.type)
        );

        WriteIf(
            e.position != default,
            () => w.Write(EventField.position, e.position)
        );

        WriteIf(
            e.rotation != default,
            () => w.Write(EventField.rotation, e.rotation)
        );

        WriteIf(
            !string.IsNullOrEmpty(e.masterId),
            () => w.Write(EventField.masterId, e.masterId)
        );

        WriteIf(
            e.playerIndex > -1,
            () => w.Write(EventField.playerIndex, e.playerIndex)
        );

        WriteIf(
            e.damage != 0,
            () => w.Write(EventField.damage, (byte)e.damage)
        );

        WriteIf(
            e.fxType != FXOneShotType.None,
            () => w.Write(EventField.fxType, (byte)e.fxType)
        );

        WriteIf(
            e.structureId != -1,
            () => w.Write(EventField.structureId, e.structureId)
        );

        return (int)(w.BaseStream.Position - start);
    }
    
    // ----- Serialization -----
    
    public static byte[] SerializeReplayFile(ReplayInfo replay)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        bw.Write(Encoding.ASCII.GetBytes("RPLY"));
        
        StructureState[] lastStructureFrame = null;
        PlayerState[] lastPlayerFrame = null;
        PedestalState[] lastPedestalFrame = null;
        ScenePropState[] lastScenePropFrame = null;
        
        int total = replay.Frames.Length;
        int lastLoggedPercent = -1;

        for (int i = 0; i < total; i++)
        {
            var f = replay.Frames[i];
            using var frameMs = new MemoryStream();
            using var frameBw = new BinaryWriter(frameMs);

            frameBw.Write(f.Time);

            using var entriesMs = new MemoryStream();
            using var entriesBw = new BinaryWriter(entriesMs);

            int entryCount = 0;

            int structureCount = replay.Header.Structures.Length;
            int playerCount = replay.Header.Players.Length;
            int pedestalCount = replay.Header.PedestalCount;
            int scenePropCount = replay.Header.ScenePropCount;

            lastStructureFrame ??= Utilities.NewArray<StructureState>(structureCount);
            lastPlayerFrame ??= Utilities.NewArray<PlayerState>(playerCount);
            lastPedestalFrame ??= Utilities.NewArray<PedestalState>(pedestalCount);
            lastScenePropFrame ??= Utilities.NewArray<ScenePropState>(scenePropCount);

            // Structures

            for (int j = 0; j < structureCount; j++)
            {
                if (j >= f.Structures.Length || j >= lastStructureFrame.Length)
                    continue;

                var curr = f.Structures[j];
                var prev = lastStructureFrame[j];

                long headerPos = entriesBw.BaseStream.Position;

                entriesBw.Write((byte)ChunkType.StructureState);
                entriesBw.Write(j);

                long sizePos = entriesBw.BaseStream.Position;
                entriesBw.Write(0);

                int written = WriteStructureDiff(entriesBw, prev, curr);

                if (written > 0)
                {
                    long end = entriesBw.BaseStream.Position;

                    entriesBw.BaseStream.Position = sizePos;
                    entriesBw.Write(written);
                    entriesBw.BaseStream.Position = end;

                    entryCount++;
                }
                else
                {
                    entriesBw.BaseStream.Position = headerPos;
                }

                lastStructureFrame[j] = curr;
            }

            // Players

            for (int j = 0; j < playerCount; j++)
            {
                if (j >= f.Players.Length || j >= lastPlayerFrame.Length)
                    continue;

                var curr = f.Players[j];
                var prev = lastPlayerFrame[j];

                long headerPos = entriesBw.BaseStream.Position;

                entriesBw.Write((byte)ChunkType.PlayerState);
                entriesBw.Write(j);

                long sizePos = entriesBw.BaseStream.Position;
                entriesBw.Write(0);

                int written = WritePlayerDiff(entriesBw, prev, curr);

                if (written > 0)
                {
                    long end = entriesBw.BaseStream.Position;

                    entriesBw.BaseStream.Position = sizePos;
                    entriesBw.Write(written);
                    entriesBw.BaseStream.Position = end;

                    entryCount++;
                }
                else
                {
                    entriesBw.BaseStream.Position = headerPos;
                }

                lastPlayerFrame[j] = curr;
            }

            // Pedestals
            
            for (int j = 0; j < pedestalCount; j++)
            {
                if (j >= f.Pedestals.Length || j >= lastPedestalFrame.Length)
                    continue;

                var curr = f.Pedestals[j];
                var prev = lastPedestalFrame[j];

                long headerPos = entriesBw.BaseStream.Position;

                entriesBw.Write((byte)ChunkType.PedestalState);
                entriesBw.Write(j);

                long sizePos = entriesBw.BaseStream.Position;
                entriesBw.Write(0);

                int written = WritePedestalDiff(entriesBw, prev, curr);

                if (written > 0)
                {
                    long end = entriesBw.BaseStream.Position;

                    entriesBw.BaseStream.Position = sizePos;
                    entriesBw.Write(written);
                    entriesBw.BaseStream.Position = end;

                    entryCount++;
                }
                else
                {
                    entriesBw.BaseStream.Position = headerPos;
                }

                lastPedestalFrame[j] = curr;
            }
            
            // Scene Props

            for (int j = 0; j < scenePropCount; j++)
            {
                if (j >= f.SceneProps.Length || j >= lastScenePropFrame.Length)
                    continue;

                var curr = f.SceneProps[j];
                var prev = lastScenePropFrame[j];
                
                long headerPos = entriesBw.BaseStream.Position;
                
                entriesBw.Write((byte)ChunkType.ScenePropState);
                entriesBw.Write(j);
                
                long sizePos = entriesBw.BaseStream.Position;
                entriesBw.Write(0);

                int written = WriteScenePropDiff(entriesBw, prev, curr);

                if (written > 0)
                {
                    long end = entriesBw.BaseStream.Position;
                    
                    entriesBw.BaseStream.Position = sizePos;
                    entriesBw.Write(written);
                    entriesBw.BaseStream.Position = end;
                    
                    entryCount++;
                }
                else
                {
                    entriesBw.BaseStream.Position = headerPos;
                }
                
                lastScenePropFrame[j] = curr;
            }
            
            // Events

            for (int j = 0; j < f.Events.Length; j++)
            {
                long headerPos = entriesBw.BaseStream.Position;

                entriesBw.Write((byte)ChunkType.Event);
                entriesBw.Write(j);

                long sizePos = entriesBw.BaseStream.Position;
                entriesBw.Write(0);

                int written = WriteEvent(entriesBw, f.Events[j]);

                if (written > 0)
                {
                    long end = entriesBw.BaseStream.Position;

                    entriesBw.BaseStream.Position = sizePos;
                    entriesBw.Write(written);
                    entriesBw.BaseStream.Position = end;

                    entryCount++;
                }
                else
                {
                    entriesBw.BaseStream.Position = headerPos;
                }
            }

            // Extensions
            
            foreach (var ext in ReplayAPI.Extensions)
            {
                if (!(bool)ext.Enabled.SavedValue)
                    continue;

                var writer = new ReplayAPI.FrameExtensionWriter(
                    entriesBw,
                    ext.FrameExtensionId,
                    () => entryCount++
                );

                ext.OnWriteFrame(writer, f);
            }

            // ------------
            
            frameBw.Write(entryCount);
            frameBw.Write(entriesMs.ToArray());

            byte[] frameData = frameMs.ToArray();

            bw.Write(frameData.Length);
            bw.Write(frameData);
            
            int percent = (int)((i + 1) * 100.0 / total);

            if (percent % 10 == 0 && total > 1000 && percent != lastLoggedPercent)
            {
                lastLoggedPercent = percent;
                Main.instance.LoggerInstance.Msg($"Serializing replay... {percent}%"); 
            }
        }
        
        return ms.ToArray();
    }

    // ----- Deserialization -----
    
    public static ReplayInfo LoadReplay(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        
        var manifestEntry = zip.GetEntry("manifest.json");
        if (manifestEntry == null)
        {
            Main.instance.LoggerInstance.Error("Missing manifest.json");
            return new ReplayInfo();
        }

        using var reader = new StreamReader(manifestEntry.Open());
        string manifestJson = reader.ReadToEnd();
        
        var header = JsonConvert.DeserializeObject<ReplayHeader>(manifestJson);
        
        var replayEntry = zip.GetEntry("replay");
        if (replayEntry == null)
        {
            Main.instance.LoggerInstance.Error("Missing replay");
            return new ReplayInfo();
        }

        using var ms = new MemoryStream();
        using var stream = replayEntry.Open();
        stream.CopyTo(ms);
        byte[] compressedReplay = ms.ToArray();
        
        byte[] replayData = ReplayCodec.Decompress(compressedReplay);
        
        using var memStream = new MemoryStream(replayData);
        using var br = new BinaryReader(memStream);
        
        byte[] magic = br.ReadBytes(4);
        string magicStr = Encoding.ASCII.GetString(magic);

        if (magicStr != "RPLY")
        {
            Main.instance.LoggerInstance.Error($"Invalid replay file (magic={magicStr}");
            return new ReplayInfo();
        }
        
        var replayInfo = new ReplayInfo();
        replayInfo.Header = header;
        replayInfo.Frames = ReadFrames(
            br,
            replayInfo,
            header.FrameCount,
            header.Structures.Length,
            header.Players.Length,
            header.PedestalCount,
            header.ScenePropCount
        );

        return replayInfo;
    }
    
    private static Frame[] ReadFrames(
        BinaryReader br, 
        ReplayInfo info, 
        int frameCount, 
        int structureCount, 
        int playerCount, 
        int pedestalCount,
        int scenePropCount
    )
    {
        Frame[] frames = new Frame[frameCount];

        StructureState[] lastStructures = Utilities.NewArray<StructureState>(structureCount);
        PlayerState[] lastPlayers = Utilities.NewArray<PlayerState>(playerCount);
        PedestalState[] lastPedestals = Utilities.NewArray<PedestalState>(pedestalCount);
        ScenePropState[] lastSceneProps = Utilities.NewArray<ScenePropState>(scenePropCount);
        
        for (int f = 0; f < frameCount; f++)
        {
            int frameSize = br.ReadInt32();
            
            long frameEnd = br.BaseStream.Position + frameSize;
            
            Frame frame = new Frame();
            frame.Time = br.ReadSingle();
            
            frame.Structures = Utilities.NewArray(structureCount, lastStructures);
            frame.Players = Utilities.NewArray(playerCount, lastPlayers);
            frame.Pedestals = Utilities.NewArray(pedestalCount, lastPedestals);
            frame.SceneProps = Utilities.NewArray(scenePropCount, lastSceneProps);
            var events = new List<EventChunk>();
            
            int entryCount = br.ReadInt32();

            for (int e = 0; e < entryCount; e++)
            {
                ChunkType type = (ChunkType)br.ReadByte();
                int index = br.ReadInt32();

                switch (type)
                {
                    case ChunkType.StructureState:
                    {
                        var s = ReadStructureChunk(br, lastStructures[index].Clone());
                        frame.Structures[index] = s;
                        lastStructures[index] = s;
                        break;
                    }

                    case ChunkType.PlayerState:
                    {
                        var p = ReadPlayerChunk(br, lastPlayers[index].Clone());
                        frame.Players[index] = p;
                        lastPlayers[index] = p;
                        break;
                    }

                    case ChunkType.PedestalState:
                    {
                        var p = ReadPedestalChunk(br, lastPedestals[index].Clone());
                        frame.Pedestals[index] = p;
                        lastPedestals[index] = p;
                        break;
                    }

                    case ChunkType.Event:
                    {
                        var evt = ReadEventChunk(br);
                        events.Add(evt);
                        break;
                    }

                    case ChunkType.ScenePropState:
                    {
                        var s = ReadScenePropChunk(br, lastSceneProps[index].Clone());
                        frame.SceneProps[index] = s;
                        lastSceneProps[index] = s;
                        break;
                    }

                    case ChunkType.Extension:
                    {
                        int extensionId = index;
                        int subIndex = br.ReadInt32();
                        int len = br.ReadInt32();

                        long end = br.BaseStream.Position + len;

                        var ext = ReplayAPI.Extensions.FirstOrDefault(ex => ex.FrameExtensionId == extensionId);
                        if (ext != null && (bool)ext.Enabled.SavedValue)
                            ext.OnReadFrame(br, frame, subIndex);

                        br.BaseStream.Position = end;
                        break;
                    }

                    default:
                    {
                        int len = br.ReadInt32();
                        br.BaseStream.Position += len;
                        break;
                    }
                }
            }

            frame.Events = events.ToArray();
            
            br.BaseStream.Position = frameEnd;

            frames[f] = frame;
        }
        
        return frames;
    }
    
    // ----- Chunk Reading -----
    
    /// <summary>
    /// Reads a tagged chunk from the stream and reconstructs a state object using the provided field handler.
    /// </summary>
    /// <param name="br">The <see cref="BinaryReader"/> positioned at the start of the chunk.  
    /// The method will read the chunk length and process only that region.
    /// </param>
    /// <param name="ctor">
    /// A function used to create the initial state object for this chunk.  
    /// This allows callers to supply a fresh state or clone from a previous frame
    /// to support delta-style updates
    /// </param>
    /// <param name="readField">
    /// Callback invoked for each recognized field within the chunk.  
    /// The callback receives the current state object, the field identifier, and
    /// the <see cref="BinaryReader"/> positioned at the start of that field's data  
    /// </param>
    /// <typeparam name="T">
    /// The type of the state object being constructed from that chunk.
    /// </typeparam>
    /// <typeparam name="TField">
    /// The enum type representing valid field identifiers for this chunk.
    /// </typeparam>
    /// <returns>
    /// The fully reconstructed state object after all fields in the chunk have been processed.
    /// </returns>
    public static T ReadChunk<T, TField>(
        BinaryReader br, 
        Func<T> ctor, 
        Action<T, TField, ushort, BinaryReader> readField
    ) where TField : Enum
    {
        int len = br.ReadInt32();
        long end = br.BaseStream.Position + len;

        T state = ctor();

        while (br.BaseStream.Position < end)
        {
            byte raw = br.ReadByte();
            TField id = (TField)Enum.ToObject(typeof(TField), raw);
            
            ushort size = br.ReadUInt16();
            long fieldEnd = br.BaseStream.Position + size;

            if (!Enum.IsDefined(typeof(TField), id))
            {
                br.BaseStream.Position = fieldEnd;
                continue;
            }

            readField(state, id, size, br);
            
            br.BaseStream.Position = fieldEnd;
        }

        return state;
    }
    

    static PlayerState ReadPlayerChunk(BinaryReader br, PlayerState baseState)
    {
        return ReadChunk<PlayerState, PlayerField>(
            br,
            () => baseState,
            (p, id, size, r) =>
            {
                switch (id)
                {
                    case PlayerField.VRRigPos: p.VRRigPos = r.ReadVector3(); break;
                    case PlayerField.VRRigRot: p.VRRigRot = r.ReadQuaternion(); break;
                    case PlayerField.LHandPos: p.LHandPos = r.ReadVector3(); break;
                    case PlayerField.LHandRot: p.LHandRot = r.ReadQuaternion(); break;
                    case PlayerField.RHandPos: p.RHandPos = r.ReadVector3(); break;
                    case PlayerField.RHandRot: p.RHandRot = r.ReadQuaternion(); break;
                    case PlayerField.HeadPos: p.HeadPos = r.ReadVector3(); break;
                    case PlayerField.HeadRot: p.HeadRot = r.ReadQuaternion(); break;
                    case PlayerField.currentStack: p.currentStack = r.ReadInt16(); break;
                    case PlayerField.Health: p.Health = r.ReadInt16(); break;
                    case PlayerField.active: p.active = r.ReadBoolean(); break;
                    case PlayerField.activeShiftstoneVFX: 
                        p.activeShiftstoneVFX = (PlayerShiftstoneVFX)r.ReadByte(); break;
                    case PlayerField.leftShiftstone: p.leftShiftstone = r.ReadByte(); break;
                    case PlayerField.rightShiftstone: p.rightShiftstone = r.ReadByte(); break;
                    case PlayerField.lgripInput: p.lgripInput = r.ReadSingle(); break;
                    case PlayerField.lthumbInput: p.lthumbInput = r.ReadSingle(); break;
                    case PlayerField.lindexInput: p.lindexInput = r.ReadSingle(); break;
                    case PlayerField.rindexInput: p.rindexInput = r.ReadSingle(); break;
                    case PlayerField.rthumbInput: p.rthumbInput = r.ReadSingle(); break;
                    case PlayerField.rgripInput: p.rgripInput = r.ReadSingle(); break;
                    case PlayerField.rockCamActive: p.rockCamActive = r.ReadBoolean(); break;
                    case PlayerField.rockCamPos: p.rockCamPos = r.ReadVector3(); break;
                    case PlayerField.rockCamRot: p.rockCamRot = r.ReadQuaternion(); break;
                    case PlayerField.armSpan: p.ArmSpan = r.ReadSingle(); break;
                    case PlayerField.length: p.Length = r.ReadSingle(); break;

                    case PlayerField.visualData:
                    {
                        var bytes = r.ReadBytes(size);
                        p.visualData = Encoding.UTF8.GetString(bytes);
                        break;
                    }
                }
            }
        );
    }

    static StructureState ReadStructureChunk(BinaryReader br, StructureState baseState)
    {
        return ReadChunk<StructureState, StructureField>(
            br,
            () => baseState,
            (s, id, size, r) =>
            {
                switch (id)
                {
                    case StructureField.position: s.position = r.ReadVector3(); break;
                    case StructureField.rotation: s.rotation = r.ReadQuaternion(); break;
                    case StructureField.active: s.active = r.ReadBoolean(); break;
                    case StructureField.isFlicked: s.isFlicked = r.ReadBoolean(); break;
                    case StructureField.isLeftHeld: s.isLeftHeld = r.ReadBoolean(); break;
                    case StructureField.isRightHeld: s.isRightHeld = r.ReadBoolean(); break;
                    case StructureField.currentState: s.currentState = (Structure.PhysicsState)r.ReadByte(); break;
                    case StructureField.isTargetDisk: s.isTargetDisk = r.ReadBoolean(); break;
                }
            }
        );
    }

    static PedestalState ReadPedestalChunk(BinaryReader br, PedestalState baseState)
    {
        return ReadChunk<PedestalState, PedestalField>(
            br,
            () => baseState,
            (p, id, size, r) =>
            {
                switch (id)
                {
                    case PedestalField.position: p.position = r.ReadVector3(); break;
                    case PedestalField.active: p.active = r.ReadBoolean(); break;
                }
            }
        );
    }

    static EventChunk ReadEventChunk(BinaryReader br)
    {
        return ReadChunk<EventChunk, EventField>(
            br,
            () => new EventChunk(),
            (e, id, size, r) =>
            {
                switch (id)
                {
                    case EventField.type: e.type = (EventType)r.ReadByte(); break;
                    case EventField.position: e.position = r.ReadVector3(); break;
                    case EventField.rotation: e.rotation = r.ReadQuaternion(); break;
                    case EventField.masterId: e.masterId = r.ReadString(); break;
                    case EventField.playerIndex: e.playerIndex = r.ReadInt32(); break;
                    case EventField.damage: e.damage = r.ReadByte(); break;
                    case EventField.fxType: e.fxType = (FXOneShotType)r.ReadByte(); break;
                    case EventField.structureId: e.structureId = r.ReadInt32(); break;
                }
            }
        );
    }

    static ScenePropState ReadScenePropChunk(BinaryReader br, ScenePropState baseState)
    {
        return ReadChunk<ScenePropState, ScenePropField>(
            br,
            () => baseState,
            (s, id, size, r) =>
            {
                switch (id)
                {
                    case ScenePropField.position: s.position = r.ReadVector3(); break;
                    case ScenePropField.rotation: s.rotation = r.ReadQuaternion(); break;
                }
            }
        );
    }
}

[Serializable]
public class ReplayInfo
{
    public ReplaySerializer.ReplayHeader Header;
    public Frame[] Frames;
}

[Serializable]
public class Frame
{
    public float Time;
    public StructureState[] Structures;
    public PlayerState[] Players;
    public PedestalState[] Pedestals;
    public EventChunk[] Events;
    public ScenePropState[] SceneProps;

    private Dictionary<int, object> ExtensionData = new();

    public Frame Clone()
    {
        var frame = new Frame();
        frame.Time = Time;
        frame.Structures = Utilities.NewArray(Structures.Length, Structures);
        frame.Players = Utilities.NewArray(Players.Length, Players);
        frame.Pedestals = Utilities.NewArray(Pedestals.Length, Pedestals);
        frame.Events = Utilities.NewArray(Events.Length, Events);
        frame.SceneProps = Utilities.NewArray(SceneProps.Length, SceneProps);

        return frame;
    }

    public void SetExtensionData(ReplayExtension extension, object data)
    {
        ExtensionData[extension.FrameExtensionId] = data;
    }

    public bool TryGetExtensionData<T>(ReplayExtension extension, out T value)
    {
        if (ExtensionData.TryGetValue(extension.FrameExtensionId, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }
}

// ------- Structure State -------

[Serializable]
public class StructureState
{
    public Vector3 position;
    public Quaternion rotation;
    public bool active;
    public bool isLeftHeld;
    public bool isRightHeld;
    public bool isFlicked;
    public Structure.PhysicsState currentState;
    public bool isTargetDisk;

    public StructureState Clone()
    {
        return new StructureState
        {
            position = position,
            rotation = rotation,
            active = active,
            isLeftHeld = isLeftHeld,
            isRightHeld = isRightHeld,
            isFlicked = isFlicked,
            currentState = currentState,
            isTargetDisk = isTargetDisk
        };
    }
}

[Serializable]
public class StructureInfo
{
    public StructureType Type;
    public int structureId;
    public int? targetDamage;
}

public enum StructureField : byte
{
    position,
    rotation,
    active,
    grounded,
    isLeftHeld,
    isRightHeld,
    isFlicked,
    currentState,
    isTargetDisk
}

public enum StructureType : byte
{
    Cube,
    Pillar,
    Wall,
    Disc,
    Ball,
    CagedBall,
    LargeRock,
    SmallRock,
    TetheredCagedBall,
    WrappedWall,
    PrisonedPillar,
    DockedDisk,
    CageCube,
    Target
}

// ------- Player State -------

[Serializable]
public class PlayerState
{
    public Vector3 VRRigPos;
    public Quaternion VRRigRot;

    public Vector3 LHandPos;
    public Quaternion LHandRot;

    public Vector3 RHandPos;
    public Quaternion RHandRot;

    public Vector3 HeadPos;
    public Quaternion HeadRot;

    public short currentStack;

    public short Health;
    public bool active;

    public PlayerShiftstoneVFX activeShiftstoneVFX;
    
    public int leftShiftstone;
    public int rightShiftstone;

    public float lgripInput;
    public float lindexInput;
    public float lthumbInput;
    
    public float rgripInput;
    public float rindexInput;
    public float rthumbInput;

    public bool rockCamActive;
    public Vector3 rockCamPos;
    public Quaternion rockCamRot;

    public float ArmSpan;
    public float Length;

    public string visualData;

    public PlayerState Clone()
    {
        return new PlayerState
        {
            VRRigPos = VRRigPos,
            VRRigRot = VRRigRot,
            LHandPos = LHandPos,
            LHandRot = LHandRot,
            RHandPos = RHandPos,
            RHandRot = RHandRot,
            HeadPos = HeadPos,
            HeadRot = HeadRot,
            currentStack = currentStack,
            Health = Health,
            active = active,
            activeShiftstoneVFX = activeShiftstoneVFX,
            leftShiftstone = leftShiftstone,
            rightShiftstone = rightShiftstone,
            lgripInput = lgripInput,
            lthumbInput = lthumbInput,
            lindexInput = lindexInput,
            rgripInput = rgripInput,
            rindexInput = rindexInput,
            rthumbInput = rthumbInput,
            rockCamActive = rockCamActive,
            rockCamPos = rockCamPos,
            rockCamRot = rockCamRot,
            ArmSpan = ArmSpan,
            Length = Length,
            visualData = visualData
        };
    }
}

[Serializable]
public class PlayerInfo
{
    public byte ActorId;
    public string MasterId;
    
    public string Name;
    public int BattlePoints;
    public short[] EquippedShiftStones;
    public PlayerMeasurement Measurement;

    public bool WasHost;
    public bool IsLocal;

    public PlayerInfo(Player copyPlayer)
    {
        var player = copyPlayer.Data;

        ActorId = (byte)player.GeneralData.ActorNo;
        MasterId = player.GeneralData.PlayFabMasterId;
        Name = player.GeneralData.PublicUsername;
        BattlePoints = player.GeneralData.BattlePoints;
        EquippedShiftStones = player.EquipedShiftStones.ToArray();
        Measurement = player.PlayerMeasurement;
        WasHost = (player.GeneralData.ActorNo == PhotonNetwork.MasterClient?.ActorNumber);
        IsLocal = player.GeneralData.PlayFabMasterId == Main.LocalPlayer.Data.GeneralData.PlayFabMasterId;
    }
    
    [JsonConstructor]
    public PlayerInfo() { }
}

public enum PlayerField : byte {
    VRRigPos,
    VRRigRot,
    
    LHandPos,
    LHandRot,
    
    RHandPos,
    RHandRot,
    
    HeadPos,
    HeadRot,
    
    currentStack,
    
    Health,
    active,
    
    activeShiftstoneVFX,
    leftShiftstone,
    rightShiftstone,
    
    lgripInput,
    lindexInput,
    lthumbInput,
    
    rgripInput,
    rindexInput,
    rthumbInput,
    
    rockCamActive,
    rockCamPos,
    rockCamRot,
    
    armSpan,
    length,
    
    visualData
}

[Flags]
public enum PlayerShiftstoneVFX : byte
{
    None = 0,
    Charge = 1 << 0,
    Adamant = 1 << 1,
    Vigor = 1 << 2,
    Surge = 1 << 3
}

[Serializable]
public class VoiceTrackInfo
{
    public VoiceTrackInfo(
        int ActorId,
        string FileName,
        float StartTime
    )
    {
        this.ActorId = ActorId;
        this.FileName = FileName;
        this.StartTime = StartTime;
    }
    
    public int ActorId;
    public string FileName;
    public float StartTime;
}

public enum StackType : byte {
    None,
    Dash,
    Jump,
    Flick,
    Parry,
    HoldLeft,
    HoldRight,
    Ground,
    Straight,
    Uppercut,
    Kick,
    Explode
}

// ------- Pedestal State -------

[Serializable]
public class PedestalState
{
    public Vector3 position;
    
    public bool active;

    public PedestalState Clone()
    {
        return new PedestalState
        {
            position = position,
            active = active
        };
    }
}

public enum PedestalField : byte
{
    position,
    active
}

// ------- Event -------

[Serializable]
public class EventChunk
{
    public EventType type;
    
    // General Info
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public string masterId;
    public int playerIndex;
    public int structureId = -1;
    
    // Damage HitMarker
    public byte damage;
    
    // FX
    public FXOneShotType fxType;
}

public enum EventType : byte
{
    OneShotFX = 1
}

public enum EventField : byte
{
    type = 0,
    position = 1,
    rotation = 2,
    masterId = 3,
    playerIndex = 7,
    damage = 8,
    fxType = 9,
    structureId = 10
}

[Serializable]
public class Marker
{
    public string name { get; set; }
    public float time { get; set; }
    public float r, g, b;

    public Marker(string name, float time, Color color)
    {
        this.name = name;
        this.time = time;
        
        r = color.r;
        g = color.g;
        b = color.b;
    }

    public Vector3? position { get; set; }
    public int? PlayerIndex { get; set; }
}

[Serializable]
public enum FXOneShotType : byte
{
    None,
    StructureCollision,
    Ricochet,
    Grounded,
    GroundedSFX,
    Ungrounded,
    
    DustImpact,
    
    ImpactLight,
    ImpactMedium,
    ImpactHeavy,
    ImpactMassive,
    
    Spawn,
    Break,
    BreakDisc,
    
    RockCamSpawn,
    RockCamDespawn,
    RockCamStick,
    
    Fistbump,
    FistbumpGoin,
    
    Jump,
    Dash,
    
    Hitmarker
}

// ------- Scene Prop State -------

[Serializable]
public class ScenePropState
{
    public Vector3 position;
    public Quaternion rotation;

    public ScenePropState Clone()
    {
        return new ScenePropState
        {
            position = position,
            rotation = rotation
        };
    }
}

public enum ScenePropField : byte
{
    position,
    rotation
}

[Serializable]
public class ScenePropInfo
{
    public ScenePropType type;
}

public enum ScenePropType : byte
{
    Fruit
}

// ------------------------

public enum ChunkType : byte
{
    PlayerState = 0,
    StructureState = 1,
    PedestalState = 2,
    Event = 3,
    ScenePropState = 4,
    Extension = 250
}