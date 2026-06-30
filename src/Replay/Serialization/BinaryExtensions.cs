using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ReplayMod.Replay.Serialization;

public static class BinaryExtensions
{
    public static void Write(this BinaryWriter bw, Vector3 v)
    {
        bw.Write(v.x);
        bw.Write(v.y);
        bw.Write(v.z);
    }

    public static void Write(this BinaryWriter bw, Quaternion q)
    {
        // Smallest-three quaternion compression algorithm
        
        int maxIndex = 0;
        float maxValue = Mathf.Abs(q[0]);

        for (int i = 1; i < 4; i++)
        {
            float abs = Mathf.Abs(q[i]);
            if (abs > maxValue)
            {
                maxIndex = i;
                maxValue = abs;
            }
        }

        float sign = q[maxIndex] < 0 ? -1f : 1f;

        for (int i = 0; i < 4; i++)
        {
            if (i == maxIndex) continue;
            ushort quantized = (ushort)(Mathf.Clamp01(q[i] * sign * 0.5f + 0.5f) * ushort.MaxValue);
            bw.Write(quantized);
        }

        bw.Write((byte)maxIndex);
    }

    public static Vector3 ReadVector3(this BinaryReader br)
    {
        return new Vector3(
            br.ReadSingle(),
            br.ReadSingle(),
            br.ReadSingle()
        );
    }

    public static Quaternion ReadQuaternion(this BinaryReader br)
    {
        float[] components = new float[4];

        ushort a = br.ReadUInt16();
        ushort b = br.ReadUInt16();
        ushort c = br.ReadUInt16();
        byte droppedIndex = br.ReadByte();
        
        float[] smalls =
        {
            (a / (float)ushort.MaxValue - 0.5f) * 2f,
            (b / (float)ushort.MaxValue - 0.5f) * 2f,
            (c / (float)ushort.MaxValue - 0.5f) * 2f,
        };

        int s = 0;
        for (int i = 0; i < 4; i++)
        {
            if (i == droppedIndex) continue;
            components[i] = smalls[s++];
        }
        
        float sumSquares = 0f;
        for (int i = 0; i < 4; i++)
        {
            if (i == droppedIndex) continue;
            sumSquares += components[i] * components[i];
        }
        components[droppedIndex] = Mathf.Sqrt(1f - sumSquares);

        return new Quaternion(components[0], components[1], components[2], components[3]);
    }

    public static void Write<TField>(this BinaryWriter bw, TField field, Vector3 v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)12);
        bw.Write(v);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, Quaternion q) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)7);
        bw.Write(q);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, short v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)2);
        bw.Write(v);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, bool v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)1);
        bw.Write(v);
    }

    public static void Write<TField>(this BinaryWriter bw, TField field, float f) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)4);
        bw.Write(f);
    }

    public static void Write<TField>(this BinaryWriter bw, TField field, string s) where TField : Enum
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)bytes.Length);
        bw.Write(bytes);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, int v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)4);
        bw.Write(v);
    }

    public static void Write<TField>(this BinaryWriter bw, TField field, byte v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)1);
        bw.Write(v);
    }

    public static void Write<TField>(this BinaryWriter bw, TField field, long v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)8);
        bw.Write(v);
    }

    public static void Write<TField>(this BinaryWriter bw, TField field, double v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)8);
        bw.Write(v);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, Vector2 v) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)8);
        bw.Write(v.x);
        bw.Write(v.y);
    }
    
    public static void Write<TField>(this BinaryWriter bw, TField field, Color32 c) where TField : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)4);
        bw.Write(c.r);
        bw.Write(c.g);
        bw.Write(c.b);
        bw.Write(c.a);
    }
    
    public static void Write<TField, TEnum>(this BinaryWriter bw, TField field, TEnum value) 
        where TField : Enum 
        where TEnum : Enum
    {
        bw.Write(Convert.ToByte(field));
        bw.Write((ushort)4);
        bw.Write(Convert.ToInt32(value));
    }
}