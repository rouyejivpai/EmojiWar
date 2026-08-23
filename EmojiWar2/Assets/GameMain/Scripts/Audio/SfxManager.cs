//------------------------------------------------------------
// EmojiWar GameMain - 音效管理器
// 运行时合成简单音效（无需外部资源），通过 AudioSource 播放。
// 射击/受击/购买/波次提示。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Audio
{
    /// <summary>
    /// 程序生成音效管理器。
    /// </summary>
    public static class SfxManager
    {
        private static AudioSource s_Source = null;

        /// <summary>
        /// 初始化（由 GameEntry 调用）。
        /// </summary>
        public static void Init()
        {
            if (s_Source != null)
            {
                return;
            }

            var go = new GameObject("SfxPlayer");
            go.transform.SetParent(GameEntry.Instance.transform);
            s_Source = go.AddComponent<AudioSource>();
            s_Source.playOnAwake = false;
            s_Source.spatialBlend = 0f;   // 2D 音效
        }

        /// <summary>
        /// 播放射击音效（短促高频）。
        /// </summary>
        public static void PlayShoot()
        {
            Play(GenerateTone(1200f, 0.08f, 0.3f), 0.5f);
        }

        /// <summary>
        /// 播放受击音效（低频短促）。
        /// </summary>
        public static void PlayHit()
        {
            Play(GenerateTone(300f, 0.12f, 0.5f), 0.6f);
        }

        /// <summary>
        /// 播放购买音效（上升音阶）。
        /// </summary>
        public static void PlayBuy()
        {
            Play(GenerateTone(880f, 0.15f, 0.4f), 0.7f);
        }

        /// <summary>
        /// 播放波次提示（两声）。
        /// </summary>
        public static void PlayWave()
        {
            Play(GenerateTone(660f, 0.1f, 0.4f), 0.6f);
        }

        private static void Play(AudioClip clip, float volume)
        {
            if (s_Source == null || clip == null)
            {
                return;
            }

            s_Source.volume = volume;
            s_Source.PlayOneShot(clip);
            Object.Destroy(clip, clip.length + 0.5f);
        }

        /// <summary>
        /// 生成单音音效。
        /// </summary>
        private static AudioClip GenerateTone(float frequency, float duration, float amplitude)
        {
            int sampleRate = 44100;
            int sampleCount = (int)(sampleRate * duration);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 简易衰减包络
                float envelope = 1f - (float)i / sampleCount;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * amplitude * envelope;
            }

            var clip = AudioClip.Create("Sfx_" + frequency, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
