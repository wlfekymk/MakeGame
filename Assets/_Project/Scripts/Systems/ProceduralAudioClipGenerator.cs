using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 별도의 오디오 에셋 파일 없이, 런타임에 간단한 절차적 사운드(사인파 비프음, 화음, 노이즈 버스트,
    /// 파도 앰비언트 루프)를 생성하는 유틸리티. Unity의 AudioClip.Create로 PCM 샘플을 직접 채워 넣는다.
    /// </summary>
    public static class ProceduralAudioClipGenerator
    {
        private const int SampleRate = 44100;

        /// <summary>
        /// 지정한 주파수의 짧은 비프음을 생성한다. endFrequency를 지정하면 시작~끝 주파수가 선형으로 스윕된다
        /// (예: 상승음/하강음 효과). 시작과 끝을 사인 곡선으로 감싸(엔벨로프) 클릭 잡음을 방지한다.
        /// </summary>
        public static AudioClip CreateBeep(float frequency, float duration, float endFrequency = -1f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            float freqEnd = endFrequency > 0f ? endFrequency : frequency;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)sampleCount;
                float freq = Mathf.Lerp(frequency, freqEnd, progress);
                float envelope = Mathf.Sin(Mathf.PI * progress); // 0 -> 1 -> 0 으로 감싸 클릭음 방지
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.5f;
            }

            return BuildClip($"Beep_{frequency}", samples);
        }

        /// <summary>
        /// 여러 주파수를 동시에 재생하는 화음 사운드를 생성한다 (배 제작 단계 완료 등 축하 효과음용).
        /// </summary>
        public static AudioClip CreateChord(float[] frequencies, float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)sampleCount;
                float envelope = Mathf.Sin(Mathf.PI * progress);

                float value = 0f;
                foreach (float freq in frequencies)
                    value += Mathf.Sin(2f * Mathf.PI * freq * t);

                samples[i] = (value / frequencies.Length) * envelope * 0.5f;
            }

            return BuildClip("Chord", samples);
        }

        /// <summary>
        /// 짧은 백색 소음 버스트를 생성한다 (타격/공격/피격 효과음용). 시작이 가장 크고 빠르게 감쇠해
        /// 타격감 있는 "퍽" 소리를 낸다.
        /// </summary>
        public static AudioClip CreateNoiseBurst(float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            var random = new System.Random();

            for (int i = 0; i < sampleCount; i++)
            {
                float progress = i / (float)sampleCount;
                float envelope = 1f - progress;
                samples[i] = ((float)random.NextDouble() * 2f - 1f) * envelope * 0.4f;
            }

            return BuildClip("NoiseBurst", samples);
        }

        /// <summary>
        /// 저주파 사인파에 약한 노이즈를 겹쳐 파도가 밀려오는 느낌의 잔잔한 배경음 루프를 생성한다.
        /// 항상 같은 파형이 나오도록 고정 시드를 사용하고, 루프 이음매가 튀지 않도록 시작/끝을 페이드한다.
        /// </summary>
        public static AudioClip CreateOceanAmbientLoop(float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            var random = new System.Random(12345);

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float wave = Mathf.Sin(2f * Mathf.PI * 0.15f * t) * 0.5f + 0.5f; // 느린 파도 주기(밀물/썰물 느낌)
                float noise = ((float)random.NextDouble() * 2f - 1f) * 0.15f;
                samples[i] = (noise * wave) * 0.3f;
            }

            int fadeSamples = Mathf.Min(sampleCount / 10, SampleRate / 2);
            for (int i = 0; i < fadeSamples; i++)
            {
                float fade = i / (float)fadeSamples;
                samples[i] *= fade;
                samples[sampleCount - 1 - i] *= fade;
            }

            return BuildClip("OceanAmbient", samples);
        }

        /// <summary>생성한 샘플 배열로 모노(1채널) AudioClip을 만들어 반환한다.</summary>
        private static AudioClip BuildClip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
