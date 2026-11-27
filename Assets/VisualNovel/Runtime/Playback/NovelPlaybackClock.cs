using System;

namespace Lament.VisualNovel
{
    public sealed class NovelPlaybackClock
    {
        private float elapsed;
        private int visit = -1;
        public bool AutoMode { get; private set; }
        public bool SkipMode { get; private set; }
        public float AutoDelay { get; set; } = 1.5f;

        public void SetAutoMode(bool enabled)
        {
            AutoMode = enabled;
            SkipMode = false;
            elapsed = 0;
        }

        public void SetSkipMode(bool enabled)
        {
            SkipMode = enabled;
            AutoMode = false;
            elapsed = 0;
        }

        public void Reset()
        {
            AutoMode = SkipMode = false;
            elapsed = 0;
            visit = -1;
        }

        public void Tick(NovelSession session, float deltaTime, bool suspended, bool voicePlaying)
        {
            if (session == null || suspended || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0) return;
            int previousVisit = session.Visit;
            session.Tick(deltaTime);
            if (visit != session.Visit)
            {
                visit = session.Visit;
                elapsed = 0;
            }
            // Time consumed by a Wait node must not also advance the following dialogue.
            if (previousVisit != session.Visit || session.Status != NovelPlaybackStatus.Dialogue) return;
            bool skip = SkipMode && session.CurrentWasRead;
            bool auto = AutoMode && session.State.textComplete && !voicePlaying;
            if (!skip && !auto) { elapsed = 0; return; }
            elapsed += deltaTime;
            float threshold = skip ? 0.08f : Math.Max(0.1f, AutoDelay);
            if (elapsed < threshold) return;
            elapsed = 0;
            session.Continue();
        }
    }
}
