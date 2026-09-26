using UnityEngine;
using UnityEngine.Rendering;

namespace TheDeep.Footage
{
    /// <summary>
    /// Re-films recorded footage: a hidden camera flies the recorded path and renders into a texture
    /// for the Footage app. Its lamp is switched on only while this camera renders, so replaying
    /// footage never lights up the real world for players. Things only seen in authored footage
    /// (the creature over Team 7) live on the FootageProxy layer, which only this camera draws.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FootagePlayback : MonoBehaviour
    {
        [SerializeField] Light lamp;
        [SerializeField] Transform proxy;

        Camera cam;
        FootageClip clip;
        TheDeep.Voice.VoiceOutput voiceOut, radioOut;
        int audioCursor;

        public float Time { get; private set; }
        public bool Playing { get; set; }
        public FootageClip Clip => clip;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.enabled = false;
            lamp.enabled = false;
            if (proxy != null) proxy.gameObject.SetActive(false);
            voiceOut = MakeOutput("FootageVoice", TheDeep.Voice.VoiceOutput.Mode.Proximity);
            radioOut = MakeOutput("FootageRadio", TheDeep.Voice.VoiceOutput.Mode.Radio);
        }

        TheDeep.Voice.VoiceOutput MakeOutput(string name, TheDeep.Voice.VoiceOutput.Mode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var output = go.AddComponent<TheDeep.Voice.VoiceOutput>();
            output.Init(mode, spatial: false);
            return output;
        }

        /// <summary>Plays the sound recorded between the last update and now.</summary>
        void PlayAudioUpTo(float time)
        {
            float volume = TheDeep.Core.GameSettings.VoiceVolume;
            while (audioCursor < clip.Audio.Count && clip.Audio[audioCursor].Time <= time)
            {
                var a = clip.Audio[audioCursor++];
                var samples = new float[a.Samples.Length];
                for (int i = 0; i < samples.Length; i++) samples[i] = TheDeep.Voice.VoiceCodec.Decode(a.Samples[i]);
                if (a.Channel == FootageAudio.Radio)
                {
                    radioOut.Configure(volume, a.Signal / 255f, false);
                    radioOut.Push(samples);
                }
                else
                {
                    // Everything the helmet mic heard was underwater.
                    voiceOut.Configure(volume, 1f, true);
                    voiceOut.Push(samples);
                }
            }
        }

        void ResetAudio()
        {
            voiceOut.Clear();
            radioOut.Clear();
            audioCursor = 0;
            if (clip == null) return;
            while (audioCursor < clip.Audio.Count && clip.Audio[audioCursor].Time < Time) audioCursor++;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeRender;
            RenderPipelineManager.endCameraRendering += AfterRender;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            RenderPipelineManager.endCameraRendering -= AfterRender;
        }

        public void Load(FootageClip footage, RenderTexture target)
        {
            clip = footage;
            cam.targetTexture = target;
            Time = 0f;
            Playing = true;
            ResetAudio();
        }

        public void Stop()
        {
            Playing = false;
            clip = null;
            cam.enabled = false;
            ResetAudio();
        }

        public void Seek(float t)
        {
            Time = clip == null ? 0f : Mathf.Clamp(t, 0f, clip.Duration);
            ResetAudio();
        }

        void Update()
        {
            if (clip == null)
            {
                cam.enabled = false;
                return;
            }
            cam.enabled = true;
            if (Playing)
            {
                Time += UnityEngine.Time.deltaTime;
                PlayAudioUpTo(Time);
                if (Time >= clip.Duration)
                {
                    Time = clip.Duration;
                    Playing = false;
                }
            }
            var frame = clip.Sample(Time);
            transform.SetPositionAndRotation(frame.Position, frame.Rotation);
            if (proxy != null)
            {
                proxy.gameObject.SetActive(frame.Proxy);
                if (frame.Proxy) proxy.SetPositionAndRotation(frame.ProxyPosition, frame.ProxyRotation);
            }
            lampWanted = frame.Lamp;
        }

        bool lampWanted;

        void BeforeRender(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering == cam) lamp.enabled = lampWanted;
        }

        void AfterRender(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering == cam) lamp.enabled = false;
        }
    }
}
