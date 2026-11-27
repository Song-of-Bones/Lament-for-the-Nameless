using UnityEngine;

namespace Lament.VisualNovel
{
    // Story volume commands set a logical level per bus; player preferences scale every bus.
    public sealed class NovelAudioChannels
    {
        private readonly AudioSource[] sources;
        private readonly float[] level = { 1f, 1f, 1f, 1f };
        private readonly float[] from = new float[4];
        private readonly float[] to = new float[4];
        private readonly float[] elapsed = new float[4];
        private readonly float[] duration = new float[4];
        private readonly NovelEase[] easing = new NovelEase[4];
        private string currentAmbient;
        public AudioSource Ambient => sources[1];

        public NovelAudioChannels(AudioSource music, AudioSource voice, AudioSource sound)
        {
            var ambient = music.gameObject.AddComponent<AudioSource>();
            ambient.playOnAwake = false; ambient.loop = true;
            sources = new[] { music, ambient, voice, sound };
            GameSettings.Changed += ApplyPreferences;
        }

        private static float Master(int bus) => GameSettings.GetVolume((NovelAudioBus)bus);

        public void Dispose() => GameSettings.Changed -= ApplyPreferences;

        private void ApplyPreferences()
        {
            for (int i = 0; i < 4; i++) sources[i].volume = level[i] * Master(i);
        }

        public void Reset()
        {
            currentAmbient = null;
            for (int i = 0; i < 4; i++)
            {
                sources[i].Stop();
                level[i] = 1f;
                sources[i].volume = Master(i);
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
                if (duration[i] > 0) continue;
                level[i] = state.volumes[i];
                sources[i].volume = level[i] * Master(i);
            }
        }

        public void Execute(NovelNodeAsset node)
        {
            if (node.command != NovelCommandType.AudioVolume) return;
            int bus = (int)node.audioBus;
            from[bus] = level[bus];
            to[bus] = node.volume;
            easing[bus] = node.easing;
            elapsed[bus] = 0; duration[bus] = node.seconds;
            if (node.seconds <= 0)
            {
                level[bus] = node.volume;
                sources[bus].volume = level[bus] * Master(bus);
            }
        }

        public void Tick(float delta, bool suspended)
        {
            if (suspended) return;
            for (int i = 0; i < 4; i++)
            {
                if (duration[i] <= 0) continue;
                elapsed[i] = Mathf.Min(duration[i], elapsed[i] + delta);
                level[i] = Mathf.Lerp(from[i], to[i], NovelStageEffects.Ease(easing[i], elapsed[i] / duration[i]));
                sources[i].volume = level[i] * Master(i);
                if (elapsed[i] >= duration[i]) duration[i] = 0;
            }
        }
    }
}
