using UnityEngine;

namespace Lament.VisualNovel
{
    public sealed class NovelAudioChannels
    {
        private readonly AudioSource[] sources;
        private readonly float[] elapsed = new float[4];
        private readonly float[] duration = new float[4];
        private readonly float[] from = new float[4];
        private readonly float[] to = new float[4];
        private readonly NovelEase[] easing = new NovelEase[4];
        private string currentAmbient;
        public AudioSource Ambient => sources[1];

        public NovelAudioChannels(AudioSource music, AudioSource voice, AudioSource sound)
        {
            var ambient = music.gameObject.AddComponent<AudioSource>();
            ambient.playOnAwake = false; ambient.loop = true;
            sources = new[] { music, ambient, voice, sound };
        }

        public void Reset()
        {
            currentAmbient = null;
            for (int i = 0; i < 4; i++)
            {
                sources[i].Stop(); sources[i].volume = 1;
                duration[i] = elapsed[i] = 0;
            }
        }

        public void Apply(NovelStoryAsset story, NovelPresentationState state, bool restoring)
        {
            state.EnsureDefaults();
            if (restoring || currentAmbient != state.ambient)
            {
                currentAmbient = state.ambient;
                Ambient.Stop(); Ambient.clip = story.FindResource(state.ambient)?.audio;
                if (Ambient.clip != null) Ambient.Play();
            }
            for (int i = 0; i < 4; i++)
            {
                if (restoring) duration[i] = 0;
                if (duration[i] <= 0) sources[i].volume = state.volumes[i];
            }
        }

        public void Execute(NovelNodeAsset node)
        {
            if (node.command != NovelCommandType.AudioVolume) return;
            int bus = (int)node.audioBus;
            from[bus] = sources[bus].volume;
            to[bus] = node.volume;
            easing[bus] = node.easing;
            elapsed[bus] = 0; duration[bus] = node.seconds;
            if (node.seconds <= 0) sources[bus].volume = node.volume;
        }

        public void Tick(float delta, bool suspended)
        {
            if (suspended) return;
            for (int i = 0; i < 4; i++)
            {
                if (duration[i] <= 0) continue;
                elapsed[i] = Mathf.Min(duration[i], elapsed[i] + delta);
                sources[i].volume = Mathf.Lerp(from[i], to[i], NovelStageEffects.Ease(easing[i], elapsed[i] / duration[i]));
                if (elapsed[i] >= duration[i]) duration[i] = 0;
            }
        }
    }
}
