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
    }

    /// <summary>A piece of sound picked up while filming (8 kHz mu-law, like the radio).</summary>
    public struct FootageAudio
    {
        public const byte OwnVoice = 0, Nearby = 1, Radio = 2;

        public float Time;
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
        public string Title = "";
        public string Timestamp = "";
        public int Diver;
        public bool EndsInDeath;
        public bool Corrupted;
        public readonly List<FootageFrame> Frames = new();
        /// <summary>Voices heard while filming, in time order.</summary>
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

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
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
                w.Write(f.Proxy);
                if (!f.Proxy) continue;
                Write(w, f.ProxyPosition);
                Write(w, f.ProxyRotation.eulerAngles);
            }
            w.Write(Audio.Count);
            foreach (var a in Audio)
            {
                w.Write(a.Time);
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
                var a = new FootageAudio { Time = r.ReadSingle(), Channel = r.ReadByte(), Signal = r.ReadByte() };
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
