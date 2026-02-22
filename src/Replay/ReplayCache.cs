using System;
using System.Collections.Generic;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Pools;
using ReplayMod.Replay.Serialization;
using UnityEngine;

namespace ReplayMod.Replay;

public static class ReplayCache
{
    public static Dictionary<string, StackType> NameToStackType = new (StringComparer.OrdinalIgnoreCase)
    {
        { "RockSlide", StackType.Dash },
        { "Jump", StackType.Jump },
        { "Flick", StackType.Flick },
        { "Parry", StackType.Parry },
        { "HoldLeft", StackType.HoldLeft },
        { "HoldRight", StackType.HoldRight },
        { "Stomp", StackType.Ground },
        { "Straight", StackType.Straight },
        { "Uppercut", StackType.Uppercut },
        { "Kick", StackType.Kick },
        { "Explode", StackType.Explode }
    };
    
    public static readonly Dictionary<string, FXOneShotType> AudioCallToFX = new()
    {
        { "Call_Structure_Impact_Light", FXOneShotType.ImpactLight },
        { "Call_Structure_Impact_Medium", FXOneShotType.ImpactMedium },
        { "Call_Structure_Impact_Heavy", FXOneShotType.ImpactHeavy },
        { "Call_Structure_Impact_Massive", FXOneShotType.ImpactMassive },
        { "Call_Structure_Ground", FXOneShotType.GroundedSFX },
        { "Call_RockCam_Spawn", FXOneShotType.RockCamSpawn },
        { "Call_RockCam_Despawn", FXOneShotType.RockCamDespawn },
        { "Call_RockCam_Stick", FXOneShotType.RockCamStick },
        { "Call_Bodyhit_Hard", FXOneShotType.Fistbump },
        { "Call_FistBumpBonus", FXOneShotType.FistbumpGoin }
    };
    
    public static readonly Dictionary<string, FXOneShotType> VFXNameToFX = new()
    {
        { "StructureCollision_VFX", FXOneShotType.StructureCollision },
        { "Ricochet_VFX", FXOneShotType.Ricochet },
        { "Ground_VFX", FXOneShotType.Grounded },
        { "Unground_VFX", FXOneShotType.Ungrounded },
        { "DustImpact_VFX", FXOneShotType.DustImpact },
        { "DustSpawn_VFX", FXOneShotType.Spawn },
        { "DustBreak_VFX", FXOneShotType.Break },
        { "DustBreakDISC_VFX", FXOneShotType.BreakDisc },
        { "RockCamSpawn_VFX", FXOneShotType.RockCamSpawn },
        { "RockCamDespawn_VFX", FXOneShotType.RockCamDespawn },
        { "PlayerBoxInteractionVFX",FXOneShotType.Fistbump },
        { "FistbumpCoin", FXOneShotType.FistbumpGoin },
    };
    
    public static readonly Dictionary<FXOneShotType, string> FXToVFXName = new()
    {
        { FXOneShotType.StructureCollision, "StructureCollision_VFX" },
        { FXOneShotType.Ricochet, "Ricochet_VFX" },
        { FXOneShotType.Grounded, "Ground_VFX" },
        { FXOneShotType.Ungrounded, "Unground_VFX" },
        { FXOneShotType.DustImpact, "DustImpact_VFX" },
        { FXOneShotType.Spawn, "DustSpawn_VFX" },
        { FXOneShotType.Break, "DustBreak_VFX" },
        { FXOneShotType.BreakDisc, "DustBreakDISC_VFX" },
        { FXOneShotType.RockCamSpawn, "RockCamSpawn_VFX" },
        { FXOneShotType.RockCamDespawn, "RockCamDespawn_VFX" },
        { FXOneShotType.Fistbump, "PlayerBoxInteractionVFX" },
        { FXOneShotType.FistbumpGoin, "FistbumpCoin" },
        { FXOneShotType.Jump, "Jump_VFX" },
        { FXOneShotType.Dash, "Dash_VFX" }
    };

    public static readonly Dictionary<FXOneShotType, string> FXToSFXName = new()
    {
        { FXOneShotType.ImpactLight, "Call_Structure_Impact_Light" },
        { FXOneShotType.ImpactMedium, "Call_Structure_Impact_Medium" },
        { FXOneShotType.ImpactHeavy, "Call_Structure_Impact_Heavy" },
        { FXOneShotType.ImpactMassive, "Call_Structure_Impact_Massive" },
        { FXOneShotType.GroundedSFX, "Call_Structure_Ground" },
        { FXOneShotType.RockCamSpawn, "Call_RockCam_Spawn" },
        { FXOneShotType.RockCamDespawn, "Call_RockCam_Despawn" },
        { FXOneShotType.RockCamStick, "Call_RockCam_Stick" },
        { FXOneShotType.Fistbump, "Call_Bodyhit_Hard" },
        { FXOneShotType.FistbumpGoin, "Call_FistBumpBonus" }
    };
    
    public static Dictionary<StructureType, Pool<PooledMonoBehaviour>> structurePools;
    public static Dictionary<string, AudioCall> SFX;
    
    public static void BuildCacheTables()
    {
        structurePools = new();
        SFX = new();

        // Pool Cache
        foreach (var pool in PoolManager.instance.availablePools)
        {
            var name = pool.poolItem.resourceName;

            if (name.Contains("RockCube")) structurePools[StructureType.Cube] = pool;
            else if (name.Contains("Pillar")) structurePools[StructureType.Pillar] = pool;
            else if (name.Contains("Disc")) structurePools[StructureType.Disc] = pool;
            else if (name.Contains("Wall")) structurePools[StructureType.Wall] = pool;
            else if (name == "Ball") structurePools[StructureType.Ball] = pool;
            else if (name.Contains("LargeRock")) structurePools[StructureType.LargeRock] = pool;
            else if (name.Contains("SmallRock")) structurePools[StructureType.SmallRock] = pool;
            else if (name.Contains("BoulderBall")) {
                structurePools[StructureType.CagedBall] = pool;
                structurePools[StructureType.TetheredCagedBall] = pool;
            }
        }
        
        AudioCall[] audioCalls = Resources.FindObjectsOfTypeAll<AudioCall>();

        foreach (var audioCall in audioCalls)
        {
            if (audioCall == null || string.IsNullOrEmpty(audioCall.name))
                continue;
            
            SFX[audioCall.name] = audioCall;
        }
    }
}