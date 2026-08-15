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
        /// 품질 개선(#18): 예전엔 단일 주기(0.15Hz)만 써서 duration이 짧을 땐(4초) 물결이 "쿵-쿵-쿵"
        /// 일정한 간격으로 반복되는 티가 났다. 배수 관계가 아닌 두 번째 훨씬 느린 주기(0.037Hz, 약 27초
        /// 주기)를 다른 비중으로 섞어, 느린 너울과 빠른 잔물결이 서로 다른 위상으로 겹치면서 매번 살짝
        /// 다른 세기로 밀려오는 불균일한 파도처럼 들리게 했다. 두 주기가 duration과 정수배로 안 맞아도
        /// 문제없다 - 아래 fade 처리가 시작/끝을 항상 0으로 수렴시키므로 루프 이음매에서 위상이 얼마든
        /// 클릭 없이 자연스럽게 이어진다.
        /// </summary>
        public static AudioClip CreateOceanAmbientLoop(float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            var random = new System.Random(12345);

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float fastWave = Mathf.Sin(2f * Mathf.PI * 0.15f * t) * 0.5f + 0.5f;   // 빠른 잔물결 주기(약 6.7초)
                float slowWave = Mathf.Sin(2f * Mathf.PI * 0.037f * t) * 0.5f + 0.5f;  // 느린 너울 주기(약 27초)
                float wave = fastWave * 0.6f + slowWave * 0.4f; // 두 주기를 섞어 불균일한 파도 세기를 만든다
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

        /// <summary>
        /// 고주파 백색 소음을 살짝 저역통과(이전 샘플과 섞기)해 날카로운 화이트노이즈보다 부드러운
        /// "쏴아" 하는 빗소리 질감의 배경음 루프를 생성한다. 파도 앰비언트와 달리 밀물/썰물 같은
        /// 느린 주기 없이 일정한 세기로 계속된다(비가 오는 동안 지속되는 소리이므로).
        /// </summary>
        public static AudioClip CreateRainAmbientLoop(float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            var random = new System.Random(54321);

            float prev = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                prev = prev * 0.7f + noise * 0.3f; // 단순 저역통과 필터로 거친 노이즈를 부드럽게 다듬는다.
                samples[i] = prev * 0.22f;
            }

            int fadeSamples = Mathf.Min(sampleCount / 10, SampleRate / 2);
            for (int i = 0; i < fadeSamples; i++)
            {
                float fade = i / (float)fadeSamples;
                samples[i] *= fade;
                samples[sampleCount - 1 - i] *= fade;
            }

            return BuildClip("RainAmbient", samples);
        }

        /// <summary>
        /// 사각파(square wave)를 짧게 끊어 반복하는 "삐-삐" 부저음을 생성한다. 실패/거부/경고 신호 전용이다.
        ///
        /// 왜 사각파인가: 이 프로젝트의 다른 효과음은 전부 사인파 비프/화음(채집·제작·치료·취침·점화)
        /// 아니면 백색 노이즈 버스트(타격) 둘 중 하나다. 사인파끼리는 주파수만 다르면 빠르게 스치는
        /// 0.1초 안에서 서로 구분되지 않는다 - 실제로 "제작 성공음 재사용" 문제(#18)가 그래서 생겼다.
        /// 사각파는 홀수 배음이 그대로 살아 있어 음색 자체가 다르므로, 주파수가 겹쳐도 "부저"로 들린다.
        /// 게임 안에서 사각파를 쓰는 소리는 이것 하나뿐이라 다른 어떤 효과음과도 혼동되지 않는다.
        /// </summary>
        /// <param name="frequency">부저 기본 주파수(Hz). 낮을수록 둔탁한 "거부" 느낌이 강해진다.</param>
        /// <param name="duration">전체 길이(초). 반복 횟수만큼 균등 분할된다.</param>
        /// <param name="repeats">끊어 울리는 횟수(1 이상). 2면 "삐-삐".</param>
        /// <param name="amplitude">최대 진폭(0~1). 사각파는 같은 진폭의 사인파보다 체감 음량이 커서 기본값을 낮게 잡았다.</param>
        public static AudioClip CreateBuzz(float frequency, float duration, int repeats = 2, float amplitude = 0.3f)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            int pulseCount = Mathf.Max(1, repeats);
            int samplesPerPulse = Mathf.Max(1, sampleCount / pulseCount);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;

                // 한 펄스 안에서의 진행도(0~1). 앞 70%만 소리를 내고 뒤 30%는 무음으로 둬서 "삐-삐"처럼 끊는다.
                float pulseProgress = (i % samplesPerPulse) / (float)samplesPerPulse;
                if (pulseProgress > 0.7f)
                {
                    samples[i] = 0f;
                    continue;
                }

                // 펄스 내부에서도 시작/끝을 사인 곡선으로 감싸 클릭 잡음을 막는다(CreateBeep과 같은 방식).
                float envelope = Mathf.Sin(Mathf.PI * (pulseProgress / 0.7f));
                float square = Mathf.Sin(2f * Mathf.PI * frequency * t) >= 0f ? 1f : -1f;
                samples[i] = square * envelope * amplitude;
            }

            return BuildClip($"Buzz_{frequency}", samples);
        }

        /// <summary>
        /// 두 주파수가 아주 좁은 간격으로 동시에 울려 "우웅-" 하는 맥놀이(beating)를 만드는 경고음을 생성한다.
        /// 상태 이상이 시작된 순간처럼 "불길한 일이 방금 일어났다"를 알릴 때 쓴다.
        ///
        /// CreateChord와의 차이: CreateChord는 협화음(523/659/784 = C장3화음)이라 성취/축하로 들린다.
        /// 여기서는 일부러 반음 이내로 붙인 두 음을 겹쳐 위상이 서로 어긋나며 음량이 주기적으로 흔들리게
        /// 만든다 - 같은 화음 계열이지만 귀에는 정반대(불안정)로 들려 축하음과 절대 헷갈리지 않는다.
        /// </summary>
        /// <param name="frequency">기준 주파수(Hz).</param>
        /// <param name="beatOffset">기준 주파수에 더할 간격(Hz). 작을수록 맥놀이가 느리고 불길해진다.</param>
        /// <param name="duration">전체 길이(초).</param>
        public static AudioClip CreateWarningBeat(float frequency, float beatOffset, float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)sampleCount;

                // 뒤로 갈수록 빠르게 잦아드는 엔벨로프(시작이 가장 크다) - 경고는 첫 순간이 가장 중요하다.
                float envelope = Mathf.Sin(Mathf.PI * progress) * (1f - progress * 0.5f);
                float value = Mathf.Sin(2f * Mathf.PI * frequency * t)
                            + Mathf.Sin(2f * Mathf.PI * (frequency + beatOffset) * t);

                samples[i] = (value * 0.5f) * envelope * 0.45f;
            }

            return BuildClip($"WarningBeat_{frequency}", samples);
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
