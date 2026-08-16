using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 외부 음원 파일을 전혀 쓰지 않고, 런타임에 순수 사인파 합성만으로 오리지널 배경음악(BGM) 루프를
    /// 생성하는 유틸리티. ProceduralAudioClipGenerator와 같은 방식(AudioClip.Create로 PCM 샘플 직접 채움)을
    /// 쓰되, 이쪽은 효과음이 아니라 여러 화음/멜로디 레이어를 겹친 "곡"을 만든다는 점이 다르다.
    /// 100% 코드로 합성한 소리라서 어떤 외부 저작권/라이선스 문제로부터도 자유롭다.
    /// </summary>
    public static class BackgroundMusicGenerator
    {
        private const int SampleRate = 44100;

        // D메이저 펜타토닉(D, E, F#, A, B)을 기본 골격으로 쓴다. 온음/장3도 위주라 화성적으로 안 부딪히고,
        // 무인도 탐험 게임 분위기에 맞는 밝고 평온한 느낌을 낸다.
        private static readonly float[] PentatonicScale =
        {
            146.83f, // D3
            164.81f, // E3
            185.00f, // F#3
            220.00f, // A3
            246.94f, // B3
            293.66f, // D4
            329.63f, // E4
            369.99f, // F#4
        };

        /// <summary>
        /// 지정한 길이의 오리지널 아일랜드 테마 BGM 루프를 합성해 반환한다.
        /// 구성: (1) 두 화음(D장조 ↔ Bm) 사이를 느리게 오가며 숨쉬듯 울리는 패드 레이어,
        /// (2) 그 위에 고정 시드로 성기게 튕기는 펜타토닉 아르페지오 레이어. 두 레이어를 합친 뒤
        /// 클리핑 방지 정규화와 루프 이음매용 페이드를 적용한다.
        /// </summary>
        public static AudioClip CreateIslandTheme(float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];

            AddPadLayer(samples, duration);
            AddArpeggioLayer(samples, duration);

            NormalizeAndFade(samples);

            AudioClip clip = AudioClip.Create("IslandTheme", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// D장조(D-F#-A) 화음과 그 관계단조인 Bm(B-D-F#) 화음 사이를 아주 느린 주기로 크로스페이드하며
        /// 겹쳐, "숨 쉬는" 듯한 지속 패드 사운드를 samples 배열에 더한다. 화음이 하나로 고정돼 있으면
        /// 단조롭게 들리므로, 화성이 천천히 움직이며 곡 전체에 은은한 진행감을 준다.
        /// </summary>
        private static void AddPadLayer(float[] samples, float duration)
        {
            float[] chordD = { 146.83f, 185.00f, 220.00f };  // D3-F#3-A3 (D장조)
            float[] chordBm = { 123.47f, 185.00f, 246.94f }; // B2-F#3-B3 (Bm)

            int sampleCount = samples.Length;
            float chordCycleSeconds = 16f; // 화음 하나당 8초씩, 왕복 16초 주기로 천천히 오간다

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;

                // 0~1 사이를 8초 주기로 코사인 곡선을 그리며 오가는 크로스페이드 계수 (급격한 전환 없이 자연스럽게 섞임)
                float cross = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / chordCycleSeconds);

                float value = 0f;
                foreach (float freq in chordD)
                    value += Mathf.Sin(2f * Mathf.PI * freq * t) * (1f - cross);
                foreach (float freq in chordBm)
                    value += Mathf.Sin(2f * Mathf.PI * freq * t) * cross;

                // 아주 느린 LFO로 볼륨을 미세하게 흔들어 정적인 드론이 아니라 "숨 쉬는" 패드처럼 들리게 한다
                float breathe = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.05f * t);

                samples[i] += (value / 3f) * breathe * 0.18f;
            }
        }

        /// <summary>
        /// 고정 시드 난수로 펜타토닉 스케일 음을 골라, 몇 초 간격으로 통기타/칼림바 같은 여린 발현음을
        /// samples 배열에 더한다. 매번 같은 패턴이 나와야 루프 경계에서 어색하지 않으므로 시드를 고정한다.
        /// </summary>
        private static void AddArpeggioLayer(float[] samples, float duration)
        {
            var random = new System.Random(20260816); // 고정 시드: 재생할 때마다 같은 멜로디 패턴이 나오게 함

            float cursor = 1.5f; // 첫 음은 곡 시작 1.5초 뒤부터 (패드가 먼저 자리잡을 시간을 줌)
            while (cursor < duration - 2f)
            {
                float noteFreq = PentatonicScale[random.Next(PentatonicScale.Length)];
                float noteDuration = 1.6f + (float)random.NextDouble() * 1.2f; // 1.6~2.8초짜리 여린 발현음
                AddPluckedNote(samples, cursor, noteFreq, noteDuration);

                // 다음 음까지 간격을 2.5~4.5초로 무작위로 둬서 기계적으로 반복되는 느낌을 없앤다
                cursor += 2.5f + (float)random.NextDouble() * 2f;
            }
        }

        /// <summary>
        /// startTime 위치부터 지수적으로 감쇠하는 단일 발현음(플럭 사운드) 하나를 samples 배열에 더한다.
        /// 배음(2배, 3배 주파수)을 옅게 섞어 순수 사인파보다 통기타/칼림바에 가까운 음색을 낸다.
        /// </summary>
        private static void AddPluckedNote(float[] samples, float startTime, float frequency, float duration)
        {
            int startSample = Mathf.RoundToInt(startTime * SampleRate);
            int noteSampleCount = Mathf.RoundToInt(duration * SampleRate);

            for (int i = 0; i < noteSampleCount; i++)
            {
                int sampleIndex = startSample + i;
                if (sampleIndex < 0 || sampleIndex >= samples.Length)
                    continue;

                float t = i / (float)SampleRate;
                float progress = i / (float)noteSampleCount;

                // 시작이 가장 크고 지수적으로 잦아드는 발현음 특유의 엔벨로프
                float envelope = Mathf.Exp(-progress * 4.5f) * Mathf.Sin(Mathf.PI * Mathf.Min(1f, progress * 12f));

                float fundamental = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float overtone2 = Mathf.Sin(2f * Mathf.PI * frequency * 2f * t) * 0.25f;
                float overtone3 = Mathf.Sin(2f * Mathf.PI * frequency * 3f * t) * 0.1f;

                samples[sampleIndex] += (fundamental + overtone2 + overtone3) * envelope * 0.22f;
            }
        }

        /// <summary>
        /// 여러 레이어를 더한 뒤 발생할 수 있는 클리핑을 막기 위해 최대 진폭이 0.9를 넘지 않도록
        /// 정규화하고, 루프 시작/끝을 짧게 페이드해 이음매에서 클릭 잡음 없이 자연스럽게 이어지게 한다.
        /// </summary>
        private static void NormalizeAndFade(float[] samples)
        {
            float peak = 0.0001f;
            for (int i = 0; i < samples.Length; i++)
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));

            if (peak > 0.9f)
            {
                float scale = 0.9f / peak;
                for (int i = 0; i < samples.Length; i++)
                    samples[i] *= scale;
            }

            int fadeSamples = Mathf.Min(samples.Length / 20, SampleRate * 2);
            for (int i = 0; i < fadeSamples; i++)
            {
                float fade = i / (float)fadeSamples;
                samples[i] *= fade;
                samples[samples.Length - 1 - i] *= fade;
            }
        }
    }
}
