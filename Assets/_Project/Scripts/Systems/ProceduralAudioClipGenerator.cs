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
        /// 물속에서 귀가 먹먹해진 듯한 수중 앰비언스 루프를 생성한다(UnderwaterAmbience의 수중 진입 시 재생용).
        ///
        /// 구성:
        ///  · 몸통: 브라운 노이즈(백색 노이즈를 1극 저역통과 필터 2단으로 거듭 다듬은 저역 웅웅거림).
        ///    파도(CreateOceanAmbientLoop)의 밝은 노이즈와 달리 고역이 거의 없어 "물이 귀를 막은" 느낌이 난다.
        ///  · 느린 진폭 LFO 2중 주기(0.11Hz/0.043Hz, 배수 관계 아님)를 섞어 웅웅거림이 일정하지 않고
        ///    천천히 밀려왔다 물러나게 한다(파도 루프와 같은 불균일화 기법).
        ///  · 간헐적 기포: 짧은 상승 사인 처프(0.05~0.12초, 저주파에서 위로 쓸어 올림) 몇 개를 루프 안
        ///    무작위 위치에 흩뿌린다. 시드 고정 System.Random이라 매번 완전히 같은 파형이 나온다(결정적).
        ///
        /// 볼륨: 최종 파형의 피크를 -18dB(≈0.126)로 정규화한다. 앰비언스는 배경에 은은하게 깔리는 소리라
        /// 다른 효과음(피크 0.3~0.5)보다 한참 낮게 잡는다 - AudioManager 쪽 bgmVolume이 다시 한 번 곱해진다.
        /// 루프 이음매는 파도/비 루프와 같은 시작/끝 페이드로 클릭 없이 이어진다(기포 처프도 페이드 구간을
        /// 피해 배치하므로 잘리지 않는다).
        /// </summary>
        public static AudioClip CreateUnderwaterAmbientLoop(float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            var random = new System.Random(24680); // 시드 고정: 실행마다 항상 같은 파형(결정적)

            // ── 몸통: 브라운 노이즈 웅웅거림 + 느린 진폭 LFO ─────────────────────────
            float lp1 = 0f;
            float lp2 = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)random.NextDouble() * 2f - 1f;

                // 1극 저역통과 2단 직렬: 계수 0.045(차단 약 320Hz)로 두 번 거르면 고역이 -24dB/oct로
                // 깎여 나가 남는 것은 저역의 둔탁한 웅웅거림뿐이다(브라운 노이즈 질감).
                lp1 += (white - lp1) * 0.045f;
                lp2 += (lp1 - lp2) * 0.045f;

                // 느린 진폭 LFO 2중 주기(약 9초/23초). 항상 양수(0.3~1.0)로 유지해 소리가 끊기진 않는다.
                float lfoFast = Mathf.Sin(2f * Mathf.PI * 0.11f * t) * 0.5f + 0.5f;
                float lfoSlow = Mathf.Sin(2f * Mathf.PI * 0.043f * t) * 0.5f + 0.5f;
                float lfo = 0.3f + 0.7f * (lfoFast * 0.55f + lfoSlow * 0.45f);

                samples[i] = lp2 * lfo;
            }

            // ── 간헐적 기포: 짧은 상승 사인 처프를 무작위 위치에 겹쳐 얹는다 ─────────
            int fadeSamples = Mathf.Min(sampleCount / 10, SampleRate / 2);
            int bubbleCount = 3 + Mathf.RoundToInt(duration); // 6초 루프 기준 9개 안팎
            for (int b = 0; b < bubbleCount; b++)
            {
                int chirpLength = Mathf.RoundToInt(SampleRate * (0.05f + 0.07f * (float)random.NextDouble()));
                // 루프 양끝 페이드 구간을 피해 배치해 처프가 페이드에 잘려 뭉개지지 않게 한다.
                int placeable = sampleCount - 2 * fadeSamples - chirpLength;
                if (placeable <= 0)
                    break;
                int start = fadeSamples + random.Next(placeable);

                float freqStart = 350f + 450f * (float)random.NextDouble(); // 기포마다 다른 음높이
                float freqEnd = freqStart * 2.1f; // 위로 쓸어 올리는 처프 = 물방울이 떠오르며 작아지는 소리
                float bubbleAmp = 0.10f + 0.08f * (float)random.NextDouble();

                for (int i = 0; i < chirpLength; i++)
                {
                    float p = i / (float)chirpLength;
                    float freq = Mathf.Lerp(freqStart, freqEnd, p);
                    float envelope = Mathf.Sin(Mathf.PI * p); // 처프 자체도 클릭 방지 엔벨로프
                    samples[start + i] += Mathf.Sin(2f * Mathf.PI * freq * (i / (float)SampleRate))
                                        * envelope * bubbleAmp;
                }
            }

            // ── 피크를 -18dB(≈0.126)로 정규화: 은은한 배경 소리로 눌러 둔다 ─────────
            float peak = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float abs = Mathf.Abs(samples[i]);
                if (abs > peak)
                    peak = abs;
            }
            if (peak > 0f)
            {
                float gain = 0.126f / peak; // -18dB = 10^(-18/20) ≈ 0.126
                for (int i = 0; i < sampleCount; i++)
                    samples[i] *= gain;
            }

            // 루프 이음매 페이드(파도/비 루프와 동일한 방식).
            for (int i = 0; i < fadeSamples; i++)
            {
                float fade = i / (float)fadeSamples;
                samples[i] *= fade;
                samples[sampleCount - 1 - i] *= fade;
            }

            return BuildClip("UnderwaterAmbient", samples);
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
