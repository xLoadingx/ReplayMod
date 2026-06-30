bl_info = {
    "name": "Replay Mod Importer",
    "author": "ERROR",
    "version": (0, 1),
    "blender": (5, 0, 0),
    "category": "3D View",
}

import bpy
import zipfile
import brotli
import json
import struct
from dataclasses import dataclass, field
from typing import List, Tuple, Optional
from enum import IntEnum, IntFlag
from io import BytesIO
import math
import copy
import re

Vector3 = Tuple[float, float, float]
Quaternion = Tuple[float, float, float, float]

class StructureField(IntEnum):
    position = 0
    rotation = 1
    active = 2
    grounded = 3
    isLeftHeld = 4
    isRightHeld = 5
    isFlicked = 6
    currentState = 7
    isTargetDisk = 8

class StructureType(IntEnum):
    Cube = 0
    Pillar = 1
    Wall = 2
    Disc = 3
    Ball = 4
    CagedBall = 5
    LargeRock = 6
    SmallRock = 7
    TetheredCagedBall = 8

class StructureStateType(IntEnum):
    Default = 0
    Free = 1
    Frozen = 2
    FreeGrounded = 3
    StableGrounded = 4
    Float = 5
    Normal = 6

@dataclass
class StructureState:
    position: Vector3 = (0, 0, 0)
    rotation: Quaternion = (0, 0, 0, 1)
    active: bool = False
    grounded: bool = False
    isLeftHeld: bool = False
    isRightHeld: bool = False
    isFlicked: bool = False
    currentState: StructureStateType = StructureStateType.Default
    isTargetDisk: bool = False


class PlayerField(IntEnum):
    VRRigPos = 0
    VRRigRot = 1
    LHandPos = 2
    LHandRot = 3
    RHandPos = 4
    RHandRot = 5
    HeadPos = 6
    HeadRot = 7
    currentStack = 8
    Health = 9
    active = 10
    activeShiftstoneVFX = 11
    leftShiftstone = 12
    rightShiftstone = 13
    lgripInput = 14
    lindexInput = 15
    lthumbInput = 16
    rgripInput = 17
    rindexInput = 18
    rthumbInput = 19
    rockCamActive = 20
    rockCamPos = 21
    rockCamRot = 22
    armSpan = 23
    length = 24

class PlayerShiftstoneVFX(IntFlag):
    None_ = 0
    Charge = 1 << 0
    Adamant = 1 << 1
    Vigor = 1 << 2
    Surge = 1 << 3

@dataclass
class PlayerState:
    VRRigPos: Vector3 = (0, 0, 0)
    VRRigRot: Quaternion = (0, 0, 0, 1)

    LHandPos: Vector3 = (0, 0, 0)
    LHandRot: Quaternion = (0, 0, 0, 1)

    RHandPos: Vector3 = (0, 0, 0)
    RHandRot: Quaternion = (0, 0, 0, 1)

    HeadPos: Vector3 = (0, 0, 0)
    HeadRot: Quaternion = (0, 0, 0, 1)

    currentStack: int = 0

    Health: int = 0
    active: bool = False

    activeShiftstoneVFX: PlayerShiftstoneVFX = PlayerShiftstoneVFX.None_

    leftShiftstone: int = 0
    rightShiftstone: int = 0

    lgripInput: float = 0.0
    lindexInput: float = 0.0
    lthumbInput: float = 0.0

    rgripInput: float = 0.0
    rindexInput: float = 0.0
    rthumbInput: float = 0.0

    rockCamActive: bool = False
    rockCamPos: Vector3 = (0, 0, 0)
    rockCamRot: Quaternion = (0, 0, 0, 1)

    ArmSpan: float = 0.0
    Length: float = 0.0

class PedestalField(IntEnum):
    position = 0
    active = 1

@dataclass
class PedestalState:
    position: Vector3 = (0, 0, 0)
    active: bool = False

class EventType(IntEnum):
    Marker = 0
    OneShotFX = 1

class EventField(IntEnum):
    type = 0
    position = 1
    rotation = 2
    masterId = 3
    markerType = 6
    playerIndex = 7
    damage = 8
    fxType = 9

class MarkerType(IntEnum):
    None_ = 0
    Manual = 1
    RoundEnd = 2
    MatchEnd = 3
    LargeDamage = 4

class FXOneShotType(IntEnum):
    None_ = 0
    StructureCollision = 1
    Ricochet = 2
    Grounded = 3
    GroundedSFX = 4
    Ungrounded = 5
    DustImpact = 6
    ImpactLight = 7
    ImpactMedium = 8
    ImpactHeavy = 9
    ImpactMassive = 10
    Spawn = 11
    Break = 12
    BreakDisc = 13
    RockCamSpawn = 14
    RockCamDespawn = 15
    RockCamStick = 16
    Fistbump = 17
    FistbumpGoin = 18
    Jump = 19
    Dash = 20

@dataclass
class EventChunk:
    type: EventType = EventType.OneShotFX

    position: Vector3 = (0, 0, 0)
    rotation: Quaternion = (0, 0, 0, 1)
    masterId: Optional[str] = None
    playerIndex: int = -1

    markerType: MarkerType = MarkerType.None_

    damage: int = 0

    fxType: FXOneShotType = FXOneShotType.None_

class ChunkType(IntEnum):
    PlayerState = 0
    StructureState = 1
    PedestalState = 2
    Event = 3
    ScenePropState = 4
    Extension = 250

@dataclass
class Frame:
    time: float
    structures: List[StructureState]
    players: List[PlayerState]
    pedestals: List[PedestalState]
    events: List[EventChunk] = field(default_factory=list)

@dataclass
class ReplayHeader:
    Title: str
    Version: str
    Scene: str
    Date: str
    Duration: float
    FrameCount: int
    PedestalCount: int
    ScenePropCount: int
    isFavorited: bool
    MarkerCount: int
    AvgPing: int
    MaxPing: int
    MinPing: int
    TargetFPS: int
    SceneProps: list
    Players: list
    Structures: list
    Markers: list
    Guid: str
    CustomMap: str = ""

@dataclass
class ReplayInfo:
    header: ReplayHeader
    frames: List[Frame]

def read_vector3(br) -> Vector3:
    return struct.unpack("<fff", br.read(12))

def read_quaternion(br) -> Quaternion:
    a, b, c = struct.unpack("<HHH", br.read(6))
    dropped_index = struct.unpack("<B", br.read(1))[0]

    smalls = [
        (a / 65535.0 - 0.5) * 2.0,
        (b / 65535.0 - 0.5) * 2.0,
        (c / 65535.0 - 0.5) * 2.0,
        ]

    components = [0.0, 0.0, 0.0, 0.0]

    s = 0
    for i in range(4):
        if i == dropped_index:
            continue
        components[i] = smalls[s]
        s += 1

    sum_squares = sum(
        components[i] * components[i]
        for i in range(4)
        if i != dropped_index
    )

    components[dropped_index] = math.sqrt(max(0.0, 1.0 - sum_squares))

    x, y, z, w = components
    return x, y, z, w

class ReplayReader:
    def __init__(self, path):
        self.path = path
        self.header = None
        self.frames = None

    def _read_chunk_header(self, br):
        pos = br.tell()
        data = br.read(9)

        if len(data) != 9:
            raise Exception(f"EOF reading chunk header at pos {pos}, got {len(data)} bytes")

        chunk_type = data[0]
        index = struct.unpack("<i", data[1:5])[0]
        chunk_len = struct.unpack("<i", data[5:9])[0]

        return chunk_type, index, chunk_len

    def _read_structure_chunk(self, br, chunk_len, base_state: StructureState) -> StructureState:
        chunk_end = br.tell() + chunk_len
        state = copy.deepcopy(base_state)

        while br.tell() < chunk_end:
            field_id_raw = struct.unpack("<B", br.read(1))[0]
            field_size = struct.unpack("<H", br.read(2))[0]
            field_end = br.tell() + field_size

            try:
                field_id = StructureField(field_id_raw)
            except ValueError:
                br.seek(field_end)
                continue

            if field_id == StructureField.position:
                state.position = read_vector3(br)

            elif field_id == StructureField.rotation:
                state.rotation = read_quaternion(br)

            elif field_id == StructureField.active:
                state.active = struct.unpack("<?", br.read(1))[0]

            elif field_id == StructureField.grounded:
                state.grounded = struct.unpack("<?", br.read(1))[0]

            elif field_id == StructureField.isLeftHeld:
                state.isLeftHeld = struct.unpack("<?", br.read(1))[0]

            elif field_id == StructureField.isRightHeld:
                state.isRightHeld = struct.unpack("<?", br.read(1))[0]

            elif field_id == StructureField.isFlicked:
                state.isFlicked = struct.unpack("<?", br.read(1))[0]

            elif field_id == StructureField.currentState:
                value = struct.unpack("<B", br.read(1))[0]
                state.currentState = StructureStateType(value)

            elif field_id == StructureField.isTargetDisk:
                state.isTargetDisk = struct.unpack("<?", br.read(1))[0]

            else:
                br.seek(field_end)
                print(f"Chunk: unknown={br.tell()}")

            br.seek(field_end)

        return state
    
    def _read_player_chunk(self, br, chunk_len, base_state: PlayerState) -> PlayerState:
        chunk_end = br.tell() + chunk_len
        state = copy.deepcopy(base_state)

        while br.tell() < chunk_end:
            field_id_raw = struct.unpack("<B", br.read(1))[0]
            field_size = struct.unpack("<H", br.read(2))[0]
            field_end = br.tell() + field_size

            try:
                field_id = PlayerField(field_id_raw)
            except ValueError:
                br.seek(field_end)
                continue
                
            if field_id == PlayerField.VRRigPos:
                state.VRRigPos = read_vector3(br)
            
            elif field_id == PlayerField.VRRigRot:
                state.VRRigRot = read_quaternion(br)
                
            elif field_id == PlayerField.HeadPos:
                state.HeadPos = read_vector3(br)
            
            elif field_id == PlayerField.HeadRot:
                state.HeadRot = read_quaternion(br)
                
            else:
                br.seek(field_end)
                print(f"Chunk: unknown={br.tell()}")
                
            br.seek(field_end)
            
        return state

    def _read_frames(self, br):
        frames = []

        frame_index = 0

        player_count = len(self.header.Players)
        structure_count = len(self.header.Structures)
        pedestal_count = self.header.PedestalCount
        frame_count = self.header.FrameCount

        last_players = [PlayerState() for _ in range(player_count)]
        last_structures = [StructureState() for _ in range(structure_count)]
        last_pedestals = [PedestalState() for _ in range(pedestal_count)]

        while True:
            raw = br.read(4)
            if not raw:
                break

            frame_size = struct.unpack("<i", raw)[0]
            frame_end = br.tell() + frame_size

            time = struct.unpack("<f", br.read(4))[0]
            entry_count = struct.unpack("<i", br.read(4))[0]

            print(f"\nFrame {frame_index}: time={time}, entries={entry_count}, size={frame_size}")

            frame_structures = [copy.deepcopy(s) for s in last_structures]
            frame_players = [copy.deepcopy(p) for p in last_players]

            for _ in range(entry_count):
                chunk_type_raw, index, chunk_len = self._read_chunk_header(br)
                chunk_type = ChunkType(chunk_type_raw)

                if chunk_type == ChunkType.StructureState:
                    new_state = self._read_structure_chunk(
                        br,
                        chunk_len,
                        last_structures[index]
                    )

                    frame_structures[index] = new_state
                    last_structures[index] = new_state
                    
                elif chunk_type == ChunkType.PlayerState:
                    new_state = self._read_player_chunk(
                        br,
                        chunk_len,
                        last_players[index]
                    )
                    
                    frame_players[index] = new_state
                    last_players[index] = new_state

                else:
                    br.seek(br.tell() + chunk_len)

            br.seek(frame_end)

            frames.append(
                Frame(
                    time = time,
                    structures = frame_structures,
                    players = [],
                    pedestals = [],
                    events = []
                )
            )

            frame_index += 1

        return frames

    def load(self) -> ReplayInfo:
        with zipfile.ZipFile(self.path, "r") as z:
            manifest_json = z.read("manifest.json")
            compressed_replay = z.read("replay")

        header_dict = json.loads(manifest_json)
        self.header = ReplayHeader(**header_dict)

        replay_bytes = brotli.decompress(compressed_replay)

        br = BytesIO(replay_bytes)

        magic = br.read(4).decode("ascii")
        if magic != "RPLY":
            raise ValueError(f"Invalid replay file (magic={magic})")

        self.frames = self._read_frames(br)

        return ReplayInfo(
            header=self.header,
            frames=self.frames
        )


class RUMBLE_OT_load_replay(bpy.types.Operator):
    bl_idname = "rumble.load_replay"
    bl_label = "Load Replay"

    def execute(self, context):
        path = context.scene.rumble_replay_path

        if not path:
            self.report({'ERROR'}, "No file selected")
            return {'CANCELLED'}

        print("Loading replay:", path)

        try:
            reader = ReplayReader(path)
            replay = reader.load()

            name = "ReplayRoot"

            if name in bpy.data.collections:
                col = bpy.data.collections[name]

                for obj in list(col.objects):
                    bpy.data.objects.remove(obj, do_unlink=True)
            else:
                col = bpy.data.collections.new(name)
                bpy.context.scene.collection.children.link(col)

            bpy.ops.object.empty_add(type='PLAIN_AXES')
            root = bpy.context.active_object
            root.name = "ReplayRoot"

            for c in root.users_collection:
                c.objects.unlink(root)
            col.objects.link(root)

            root.scale = (-1, 1, 1)
            root.rotation_euler = (math.radians(90), 0, 0)

            bpy.ops.object.empty_add(type='PLAIN_AXES')
            structureRoot = bpy.context.active_object
            structureRoot.name = "Structures"
            structureRoot.parent = root

            structure_count = len(replay.header.Structures)

            objects = []

            for i in range(structure_count):
                structure_info = replay.header.Structures[i]
                structure_type = StructureType(structure_info["Type"])

                template_name = structure_type.name
                template = bpy.data.objects.get(template_name)

                if template is None:
                    raise Exception(f"Missing template mesh: {template_name}")

                obj = template.copy()
                obj.data = template.data.copy()
                obj.rotation_mode = 'QUATERNION'
                obj.name = f"Structure{i}"

                for c in obj.users_collection:
                    c.objects.unlink(obj)
                col.objects.link(obj)

                obj.parent = structureRoot

                objects.append(obj)

            self.report({'INFO'}, f"Created {structure_count} structure(s)")

            bpy.ops.object.empty_add(type='PLAIN_AXES')
            playersRoot = bpy.context.active_object
            playersRoot.name = "Players"
            playersRoot.parent = root

            player_count = len(replay.header.Players)

            players = []

            self.report({'INFO'}, f"Loaded {player_count} player(s)")

            scene = bpy.context.scene
            scene.frame_start = 1
            scene.frame_end = len(replay.frames)

            fps = replay.header.TargetFPS
            scene.render.fps = fps

            for frame in replay.frames:
                blender_frame = round(frame.time * fps) + 1
                scene.frame_set(blender_frame)

                for i, state in enumerate(frame.structures):
                    obj = objects[i]

                    obj.location = (
                        state.position[0],
                        state.position[1],
                        state.position[2]
                    )

                    x, y, z, w = state.rotation
                    obj.rotation_quaternion = (
                        w,
                        x,
                        y,
                        z
                    )

                    obj.hide_viewport = not state.active
                    obj.hide_render = not state.active

                    obj.keyframe_insert(data_path="location")
                    obj.keyframe_insert(data_path="rotation_quaternion")
                    obj.keyframe_insert(data_path="hide_viewport")
                    obj.keyframe_insert(data_path="hide_render")

        except Exception as e:
            self.report({'ERROR'}, f"Failed to load replay: {e}")
            return {'CANCELLED'}

        self.report({'INFO'}, "Successfully loaded replay")
        return {'FINISHED'}

class RUMBLE_PT_panel(bpy.types.Panel):
    bl_label = "Replay Mod"
    bl_idname = "RUMBLE_PT_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Rumble"

    def draw(self, context):
        layout = self.layout
        scene = context.scene

        layout.prop(scene, "rumble_replay_path")
        layout.operator("rumble.load_replay")

def register():
    bpy.utils.register_class(RUMBLE_OT_load_replay)
    bpy.utils.register_class(RUMBLE_PT_panel)

    bpy.types.Scene.rumble_replay_path = bpy.props.StringProperty(
        name="Replay File",
        subtype='FILE_PATH'
    )

def unregister():
    bpy.utils.unregister_class(RUMBLE_OT_load_replay)
    bpy.utils.unregister_class(RUMBLE_PT_panel)

    del bpy.types.Scene.rumble_replay_path

if __name__ == "__main__":
    register()