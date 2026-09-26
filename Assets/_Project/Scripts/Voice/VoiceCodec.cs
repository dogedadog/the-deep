using UnityEngine;

namespace TheDeep.Voice
{
    /// <summary>
    /// 8-bit mu-law (G.711) voice encoding: telephone-quality, 1 byte per sample.
    /// At 8 kHz that's 64 kbit/s while someone talks, which suits a walkie-talkie.
    /// </summary>
    public static class VoiceCodec
    {
        public const int SampleRate = 8000;
        const int Bias = 0x84;
        const int Clip = 32635;

        public static byte Encode(float sample)
        {
            int pcm = Mathf.Clamp((int)(sample * 32767f), -32768, 32767);
            int sign = (pcm >> 8) & 0x80;
            if (sign != 0) pcm = -pcm;
            if (pcm > Clip) pcm = Clip;
            pcm += Bias;
            int exponent = 7;
            for (int mask = 0x4000; (pcm & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
            int mantissa = (pcm >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte value)
        {
            int u = ~value & 0xFF;
            int sign = u & 0x80;
            int exponent = (u >> 4) & 0x07;
            int mantissa = u & 0x0F;
            int pcm = ((mantissa << 3) + Bias) << exponent;
            pcm -= Bias;
            return (sign != 0 ? -pcm : pcm) / 32768f;
        }
    }
}
