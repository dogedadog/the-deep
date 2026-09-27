using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheDeep.Footage
{
    /// <summary>One sample of helmet-camera footage.</summary>
    public struct FootageFrame
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Lamp;
        /// <summary>Something else seen in the shot (authored footage only), drawn only by the footage camera.</summary>
        public bool Proxy;
        public Vector3 ProxyPosition;
        public Quaternion ProxyRotation;
        /// <summary>First frame of a new take (recording was stopped and started again): playback jumps here with static.</summary>
        public bool Cut;
    }

    /// <summary>A piece of sound picked up while filming (8 kHz mu-law, like the radio).</summary>
    public struct FootageAudio
    {
        /// <summary>Helmet: the suit's own sounds (breathing, bubbles, knocks), heard dry rather than through water.</summary>
        public const byte OwnVoice = 0, Nearby = 1, Radio = 2, Helmet = 3;

        /// <summary>Where the sound starts, in 8 kHz samples since the clip started.</summary>
        public int Offset;
        public byte Channel;
        public byte Signal;       // radio quality, 0-255
        public byte[] Samples;
    }

    /// <summary>
    /// Helmet-camera footage, stored as the camera's path (10 samples a second) rather than video:
    /// playback re-renders the scene from those viewpoints. Small enough to send over the network.
    /// </summary>
    public class FootageClip
    {
        /// <summary>First byte of <see cref="ToBytes"/>. Chips are never saved, so only the current build's format is read.</summary>
        const byte Format = 2;

        public string Title = "";
        public string Timestamp = "";
        public int Diver;
        public bool EndsInDeath;
        public bool Corrupted;
        public readonly List<FootageFrame> Frames = new();
        /// <summary>Sound heard while filming. Entries may overlap (several people talking at once); playback mixes them.</summary>
        public readonly List<FootageAudio> Audio = new();

        public float Duration => Frames.Count > 0 ? Frames[^1].Time : 0f;

        public FootageFrame Sample(float t)
        {
            if (Frames.Count == 0) return default;
            if (t <= Frames[0].Time) return Frames[0];
            for (int i = 1; i < Frames.Count; i++)
            {
                if (Frames[i].Time < t) continue;
                var a = Frames[i - 1];
                var b = Frames[i];
                if (b.Cut) return a; // no sliding between takes: hold the last shot until the cut
                float k = Mathf.InverseLerp(a.Time, b.Time, t);
                return new FootageFrame
                {
                    Time = t,
                    Position = Vector3.Lerp(a.Position, b.Position, k),
                    Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, k),
                    Lamp = k < 0.5f ? a.Lamp : b.Lamp,
                    Proxy = a.Proxy && b.Proxy,
                    ProxyPosition = Vector3.Lerp(a.ProxyPosition, b.ProxyPosition, k),
                    ProxyRotation = Quaternion.Slerp(a.ProxyRotation, b.ProxyRotation, k),
                };
            }
            return Frames[^1];
        }

        /// <summary>Seconds since the most recent cut at or before <paramref name="t"/> (infinity if none).</summary>
        public float SinceCut(float t)
        {
            for (int i = Frames.Count - 1; i >= 0; i--)
                if (Frames[i].Cut && Frames[i].Time <= t) return t - Frames[i].Time;
            return float.PositiveInfinity;
        }

        /// <summary>True if a cut happens after <paramref name="from"/> and at or before <paramref name="to"/>.</summary>
        public bool CutBetween(float from, float to)
        {
            foreach (var f in Frames)
                if (f.Cut && f.Time > from && f.Time <= to) return true;
            return false;
        }

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(Format);
            w.Write(Title);
            w.Write(Timestamp);
            w.Write(Diver);
            w.Write(EndsInDeath);
            w.Write(Corrupted);
            w.Write(Frames.Count);
            foreach (var f in Frames)
            {
                w.Write(f.Time);
                Write(w, f.Position);
                Write(w, f.Rotation.eulerAngles);
                w.Write(f.Lamp);
                w.Write(f.Cut);
                w.Write(f.Proxy);
                if (!f.Proxy) continue;
                Write(w, f.ProxyPosition);
                Write(w, f.ProxyRotation.eulerAngles);
            }
            w.Write(Audio.Count);
            foreach (var a in Audio)
            {
                w.Write(a.Offset);
                w.Write(a.Channel);
                w.Write(a.Signal);
                w.Write(a.Samples.Length);
                w.Write(a.Samples);
            }
            return stream.ToArray();
        }

        public static FootageClip FromBytes(byte[] data)
        {
            using var r = new BinaryReader(new MemoryStream(data));
            if (r.ReadByte() != Format) return new FootageClip { Title = "UNREADABLE CHIP", Corrupted = true };
            var clip = new FootageClip
            {
                Title = r.ReadString(),
                Timestamp = r.ReadString(),
                Diver = r.ReadInt32(),
                EndsInDeath = r.ReadBoolean(),
                Corrupted = r.ReadBoolean(),
            };
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var f = new FootageFrame
                {
                    Time = r.ReadSingle(),
                    Position = Read(r),
                    Rotation = Quaternion.Euler(Read(r)),
                    Lamp = r.ReadBoolean(),
                    Cut = r.ReadBoolean(),
                    Proxy = r.ReadBoolean(),
                };
                if (f.Proxy)
                {
                    f.ProxyPosition = Read(r);
                    f.ProxyRotation = Quaternion.Euler(Read(r));
                }
                clip.Frames.Add(f);
            }
            if (r.BaseStream.Position >= r.BaseStream.Length) return clip; // footage without sound
            int audioCount = r.ReadInt32();
            for (int i = 0; i < audioCount; i++)
            {
                var a = new FootageAudio { Offset = r.ReadInt32(), Channel = r.ReadByte(), Signal = r.ReadByte() };
                a.Samples = r.ReadBytes(r.ReadInt32());
                clip.Audio.Add(a);
            }
            return clip;
        }

        static void Write(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        static Vector3 Read(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
