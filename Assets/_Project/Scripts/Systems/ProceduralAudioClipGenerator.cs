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

        // ═════════════════════════════════════════════════════════════════════════
        //  비 사운드 3층 (RainAudio 전용) — 층 1 기본 강우 / 층 2 재질별 타격음 / 층 3은 필터
        //
        //  ── 왜 여기에 새 루프 생성기를 더 쓰는가 ──────────────────────────────
        //  기존 CreateRainAmbientLoop(12초)는 "쏴아" 한 겹뿐이라 약한 비와 폭우가 **볼륨만** 다르다.
        //  실제 비는 세기가 오르면 소리의 **스펙트럼**이 통째로 바뀌고(가는 쉬익 → 묵직한 웅웅),
        //  무엇에 부딪히느냐에 따라 완전히 다른 타격음이 얹힌다. 그 두 축을 나눠 담기 위해
        //  아래 생성기들이 필요하다. 기존 함수는 한 줄도 바꾸지 않는다(AudioManager가 계속 쓴다).
        //
        //  ── 루프 이음매 처리가 기존과 다르다(중요) ────────────────────────────
        //  이 파일의 기존 루프(파도/비/수중)는 전부 "시작·끝을 0으로 페이드"한다. 이음매의 **클릭**은
        //  확실히 없애지만, 노이즈 계열처럼 계속 울리는 소리에서는 루프 주기마다 소리가 쑥 꺼졌다
        //  돌아오는 **숨쉬기**가 남는다(실측: 8초 노이즈에 기존 방식을 적용하면 0.25초 창 RMS가
        //  최소 0.071 / 최대 0.250 = 3.5배로 출렁인다).
        //  아래 CrossfadeWrap은 대신 "N + X 샘플을 이어서 만든 뒤 꼬리 X를 머리 X에 등파워로 겹쳐
        //  접는" 방식이다. 접힌 뒤 out[0]은 raw[N], out[N-1]은 raw[N-1]이라 **두 샘플이 원래 스트림에서
        //  바로 이웃**이므로 이음매의 단차가 평범한 샘플 간 단차와 통계적으로 구별되지 않는다
        //  (실측: 이음매 단차 0.164 vs 평균 인접 단차 0.158 · 99퍼센타일 0.420). 볼륨 출렁임도
        //  1.29배까지 줄어든다(그 1.29배는 아래 gust LFO가 일부러 만든 것이다).
        //  ⚠️ 이 기법은 **노이즈처럼 위상이 무의미한 소리**에만 쓸 것. 사인/화음처럼 음정이 있는
        //  소리를 등파워로 겹치면 두 위상이 간섭해 이음매에서 음이 흔들린다.
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 길이 loopSamples + fadeSamples 로 만들어 둔 원본 스트림의 꼬리를 머리에 등파워(√)로 겹쳐
        /// 접어, 클릭도 볼륨 딥도 없는 완전 순환 루프 배열(길이 loopSamples)을 만든다.
        /// 등파워인 이유: 상관 없는 두 노이즈 구간을 선형(1-p, p)으로 겹치면 진폭이 중간에서
        /// √(p²+(1-p)²)=0.707배로 파이므로 오히려 딥이 생긴다. √p·√(1-p)를 쓰면 전력이 보존된다.
        /// </summary>
        private static float[] CrossfadeWrap(float[] raw, int loopSamples, int fadeSamples)
        {
            var outSamples = new float[loopSamples];
            System.Array.Copy(raw, outSamples, loopSamples);

            int fade = Mathf.Clamp(fadeSamples, 0, loopSamples);
            for (int i = 0; i < fade; i++)
            {
                float p = i / (float)fade;
                outSamples[i] = raw[i] * Mathf.Sqrt(p) + raw[loopSamples + i] * Mathf.Sqrt(1f - p);
            }

            return outSamples;
        }

        /// <summary>파형의 절대 피크를 targetPeak로 맞춘다(무음이면 아무것도 하지 않는다).</summary>
        private static void NormalizePeak(float[] samples, float targetPeak)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float abs = Mathf.Abs(samples[i]);
                if (abs > peak)
                    peak = abs;
            }

            if (peak <= 0.0001f)
                return;

            float gain = targetPeak / peak;
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= gain;
        }

        /// <summary>루프 이음매 크로스페이드 길이(초). 0.35초면 노이즈 상관이 완전히 끊긴다.</summary>
        private const float LoopWrapSeconds = 0.35f;

        /// <summary>
        /// [층 1] 광대역 강우 베드 루프. **강도에 따른 저역/고역 균형은 이 클립이 아니라 재생 쪽
        /// (RainAudio)의 AudioHighPassFilter/AudioLowPassFilter 컷오프가 만든다** — 약한 비 = 하이패스를
        /// 900Hz까지 올려 몸통을 깎고 쉬익만 남기고, 폭우 = 하이패스를 40Hz로 내려 묵직한 저역을 되돌린다.
        /// 클립을 세기별로 여러 장 굽지 않고 필터로 가르는 이유는 (a) 메모리·생성 시간이 1/N이고
        /// (b) 세기가 연속값(RainIntensity01)이라 클립 전환이 아니라 **연속 변형**이어야 하기 때문이다.
        /// 그러려면 원본에 저역과 고역이 **둘 다 충분히** 들어 있어야 한다:
        ///  · 고역(쉬익) : 1극 저역통과 계수 0.55(차단 약 6.7kHz)로 살짝만 다듬은 백색 노이즈.
        ///  · 저역(웅웅) : 같은 노이즈를 계수 0.030 2단(차단 약 210Hz)으로 걸러 만든 브라운 성분.
        ///    2단 필터는 진폭이 크게 줄기 때문에 6배로 되살려 섞는다(수중 앰비언스와 같은 사정).
        ///  · 돌풍(gust) : 0.083Hz(12초)·0.031Hz(32초)의 배수 아닌 두 주기를 섞은 진폭 LFO.
        ///    비가 일정한 세기로 붙박이처럼 들리지 않게 하는 최소 장치다(파도 루프와 같은 기법).
        /// 피크는 0.5로 정규화한다 — 최종 음량은 RainAudio가 bgmVolume과 세기를 곱해 정한다.
        /// </summary>
        public static AudioClip CreateRainBedLoop(float duration)
        {
            int loopSamples = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            int wrapSamples = Mathf.Min(loopSamples, Mathf.RoundToInt(SampleRate * LoopWrapSeconds));
            int total = loopSamples + wrapSamples;

            var raw = new float[total];
            var random = new System.Random(90210); // 시드 고정: 실행마다 완전히 같은 파형(결정적)

            float hiss = 0f;
            float lp1 = 0f;
            float lp2 = 0f;
            for (int i = 0; i < total; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)random.NextDouble() * 2f - 1f;

                hiss += (white - hiss) * 0.55f;   // 고역 성분(쉬익)
                lp1 += (white - lp1) * 0.030f;    // 저역 성분(웅웅) 1단
                lp2 += (lp1 - lp2) * 0.030f;      // 2단

                float gustFast = Mathf.Sin(2f * Mathf.PI * 0.083f * t) * 0.5f + 0.5f;
                float gustSlow = Mathf.Sin(2f * Mathf.PI * 0.031f * t) * 0.5f + 0.5f;
                float gust = 0.72f + 0.28f * (gustFast * 0.55f + gustSlow * 0.45f);

                raw[i] = (hiss * 0.55f + lp2 * 6f) * gust;
            }

            float[] samples = CrossfadeWrap(raw, loopSamples, wrapSamples);
            NormalizePeak(samples, 0.5f);
            return BuildClip("RainBed", samples);
        }

        /// <summary>
        /// [층 2] 재질별 타격음 루프의 공통 구현. "짧은 대역통과 노이즈 톡"을 초당 tapsPerSecond개
        /// 무작위 위치에 흩뿌리고, 필요하면 그 아래에 연속 노이즈 워시를 깐다.
        ///
        /// 대역통과는 1극 저역통과 두 개의 **차**로 만든다(y = lpFast - lpSlow). 이 파일이 이미 쓰는
        /// 1극 필터만 조합한 것이라 새 DSP 개념이 들어오지 않고, 계수 두 개로 "톡"의 음색이 정해진다:
        ///  · toneFast가 클수록 위쪽 한계가 높아져 **또렷·날카로워지고**(지붕),
        ///  · toneSlow가 클수록 아래쪽이 잘려 **가벼워진다**(잎).
        /// 각 톡은 짧은 어택(길이의 12%) 뒤 지수 감쇠라 "틱"이 아니라 "톡"으로 들린다.
        ///
        /// 톡의 위치는 완전 균등 난수다 — 균등 난수로 시각을 뽑으면 자연히 몰렸다 비었다 하는
        /// 푸아송 배치가 되어, 일정 간격으로 찍는 것보다 훨씬 "불규칙한 빗방울"로 들린다.
        /// 시드를 재질마다 다르게 줘서 세 층을 동시에 틀어도 톡이 같은 시각에 겹치지 않는다.
        /// </summary>
        /// <param name="seed">고정 시드(재질마다 다르게).</param>
        /// <param name="name">클립 이름(디버깅용).</param>
        /// <param name="duration">루프 길이(초).</param>
        /// <param name="tapsPerSecond">초당 톡 개수.</param>
        /// <param name="minTapSeconds">톡 한 개의 최소 길이(초).</param>
        /// <param name="maxTapSeconds">톡 한 개의 최대 길이(초).</param>
        /// <param name="toneFast">대역통과 위쪽 1극 계수(0~1, 클수록 밝다).</param>
        /// <param name="toneSlow">대역통과 아래쪽 1극 계수(0~1, 클수록 얇다).</param>
        /// <param name="decayShape">톡의 지수 감쇠 세기(클수록 짧고 딱딱하다).</param>
        /// <param name="washLevel">바닥에 깔 연속 노이즈 워시의 세기(0이면 없음).</param>
        /// <param name="washTone">워시용 1극 저역통과 계수(작을수록 둔탁).</param>
        private static AudioClip CreateRainImpactLoop(int seed, string name, float duration,
            float tapsPerSecond, float minTapSeconds, float maxTapSeconds,
            float toneFast, float toneSlow, float decayShape, float washLevel, float washTone)
        {
            int loopSamples = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            int wrapSamples = Mathf.Min(loopSamples, Mathf.RoundToInt(SampleRate * LoopWrapSeconds));
            int total = loopSamples + wrapSamples;

            var raw = new float[total];
            var random = new System.Random(seed);

            // ── 바닥 워시(수면처럼 "면"으로 부딪히는 재질용). 없으면 이 루프는 통째로 건너뛴다.
            if (washLevel > 0f)
            {
                float wash = 0f;
                for (int i = 0; i < total; i++)
                {
                    float white = (float)random.NextDouble() * 2f - 1f;
                    wash += (white - wash) * washTone;
                    raw[i] = wash * washLevel;
                }
            }

            // ── 톡: 대역통과 노이즈 + 어택/지수감쇠 엔벨로프를 무작위 위치에 더한다.
            int tapCount = Mathf.Max(1, Mathf.RoundToInt(tapsPerSecond * total / (float)SampleRate));
            int maxTapSamples = Mathf.Max(2, Mathf.RoundToInt(SampleRate * maxTapSeconds));
            for (int k = 0; k < tapCount; k++)
            {
                int tapLength = Mathf.RoundToInt(SampleRate *
                    Mathf.Lerp(minTapSeconds, maxTapSeconds, (float)random.NextDouble()));
                tapLength = Mathf.Clamp(tapLength, 2, maxTapSamples);

                int placeable = total - tapLength;
                if (placeable <= 0)
                    break;

                int start = random.Next(placeable);
                float amp = 0.35f + 0.65f * (float)random.NextDouble(); // 빗방울 굵기 편차
                amp *= amp; // 제곱해서 작은 톡이 많고 큰 톡이 드물게(실제 빗방울 크기 분포에 가깝다)

                float fast = 0f;
                float slow = 0f;
                for (int i = 0; i < tapLength; i++)
                {
                    float white = (float)random.NextDouble() * 2f - 1f;
                    fast += (white - fast) * toneFast;
                    slow += (fast - slow) * toneSlow;

                    float p = i / (float)tapLength;
                    float envelope = p < 0.12f
                        ? p / 0.12f                                   // 짧은 어택(클릭 방지)
                        : Mathf.Exp(-(p - 0.12f) * decayShape);       // 지수 감쇠

                    raw[start + i] += (fast - slow) * envelope * amp;
                }
            }

            float[] samples = CrossfadeWrap(raw, loopSamples, wrapSamples);
            NormalizePeak(samples, 0.5f);
            return BuildClip(name, samples);
        }

        /// <summary>
        /// [층 2 · 잎] 야자·초목 잎에 떨어지는 비. 굵은 방울이 넓은 잎을 **불규칙하게 두드리는**
        /// 소리라 톡의 밀도를 가장 낮게(초당 22개) 잡고, 길이를 길게(6~16ms) 줘서 하나하나가
        /// 개별 사건으로 들리게 한다. 음색은 중역(toneFast 0.30 / toneSlow 0.06)이라 지붕보다 둔하고
        /// 물보다 또렷하다. 워시 없음 — 잎 사이는 비어 있어 연속음이 깔리지 않는다.
        /// </summary>
        public static AudioClip CreateRainLeafLoop(float duration)
        {
            return CreateRainImpactLoop(31337, "RainOnLeaves", duration,
                tapsPerSecond: 22f, minTapSeconds: 0.006f, maxTapSeconds: 0.016f,
                toneFast: 0.30f, toneSlow: 0.06f, decayShape: 5.5f,
                washLevel: 0f, washTone: 0.2f);
        }

        /// <summary>
        /// [층 2 · 물] 바다·수면에 떨어지는 비. 물은 개별 타격이 즉시 뭉개져 **면 전체가 쉬익**거린다.
        /// 그래서 톡을 아주 많이(초당 90개) · 아주 짧게(2~5ms) · 아주 부드럽게(감쇠 14, 저역쪽 대역)
        /// 넣고, 그 아래에 연속 워시(0.22)를 깔아 개별 사건이 아니라 질감으로 들리게 한다.
        /// 세 재질 중 유일하게 워시가 있는 층이고, 그 워시가 "넓다"는 인상을 통째로 담당한다.
        /// </summary>
        public static AudioClip CreateRainWaterLoop(float duration)
        {
            return CreateRainImpactLoop(51966, "RainOnWater", duration,
                tapsPerSecond: 90f, minTapSeconds: 0.002f, maxTapSeconds: 0.005f,
                toneFast: 0.22f, toneSlow: 0.03f, decayShape: 14f,
                washLevel: 0.22f, washTone: 0.30f);
        }

        /// <summary>
        /// [층 2 · 지붕] 나무 지붕·판자에 부딪히는 비. 딱딱한 판이라 타격이 **또렷하고 밝다**:
        /// 톡을 짧게(3~8ms) 끊고 감쇠를 세게(11) 줘 "탁"에 가깝게 만들며, 대역을 위로 크게 열어
        /// (toneFast 0.62 / toneSlow 0.10) 잎·물보다 확실히 밝은 음색을 낸다. 밀도는 중간(초당 55개).
        /// 이 층이 실내 감쇠(층 3)와 맞물려 "지붕 아래에 들어왔다"를 만드는 주된 신호다.
        /// </summary>
        public static AudioClip CreateRainRoofLoop(float duration)
        {
            return CreateRainImpactLoop(60613, "RainOnRoof", duration,
                tapsPerSecond: 55f, minTapSeconds: 0.003f, maxTapSeconds: 0.008f,
                toneFast: 0.62f, toneSlow: 0.10f, decayShape: 11f,
                washLevel: 0f, washTone: 0.2f);
        }

        /// <summary>
        /// [낙수] 물방울 하나가 떨어져 고인 물에 부딪히는 "톡" 소리(RainDrips가 아주 낮은 볼륨으로
        /// 간헐 재생한다). 루프가 아니라 원샷이므로 CrossfadeWrap을 쓰지 않는다.
        ///
        /// 물방울 소리의 정체는 **떨어지는 소리가 아니라 부딪힌 뒤 생긴 기포가 울리는 소리**이고,
        /// 그 기포는 수축하면서 공명 주파수가 **올라간다**. 그래서 음높이가 위로 쓸려 올라가는
        /// 짧은 사인 처프가 물방울로 들린다(내려가면 물방울이 아니라 "뽕" 하는 다른 소리가 된다).
        /// 수중 앰비언스의 기포 처프와 같은 원리지만, 여기서는 감쇠를 훨씬 빠르게(지수) 줘서
        /// 울림 대신 **점**으로 끝나게 하고, 부딪히는 순간의 아주 짧은 노이즈 어택을 앞에 붙인다.
        ///
        /// ⚠️ CreateBeep의 sin(2π·f(t)·t) 식은 스윕에서 실제 순간 주파수가 f(t)와 달라진다(위상이
        /// t에 대해 2차식이 되기 때문). 물방울은 스윕 구간이 전부라 그 오차가 음색을 바꾸므로,
        /// 여기서는 위상을 직접 적분한다(phase += 2π·f/SampleRate).
        /// </summary>
        /// <param name="baseFrequency">시작 주파수(Hz). 방울이 작을수록 높다(520~900 정도).</param>
        /// <param name="riseRatio">끝 주파수 배율(1보다 크면 올라간다).</param>
        /// <param name="duration">전체 길이(초). 0.2초 안팎이면 "점"으로 끝난다.</param>
        public static AudioClip CreateWaterDrip(float baseFrequency, float riseRatio, float duration)
        {
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            var samples = new float[sampleCount];
            var random = new System.Random(Mathf.RoundToInt(baseFrequency)); // 시드 고정(결정적)

            float phase = 0f;
            float noiseLp = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float p = i / (float)sampleCount;

                // 순간 주파수: 앞쪽에서 빠르게 올라가고 뒤로 갈수록 완만해진다(√ 곡선).
                float freq = baseFrequency * Mathf.Lerp(1f, riseRatio, Mathf.Sqrt(p));
                phase += 2f * Mathf.PI * freq / SampleRate;

                // 진폭: 1ms 어택 뒤 지수 감쇠. 어택이 없으면 첫 샘플에서 클릭이 난다.
                float attack = Mathf.Min(1f, p * (duration / 0.001f));
                float body = Mathf.Sin(phase) * attack * Mathf.Exp(-p * 7.5f);

                // 부딪히는 순간의 아주 짧은 물 튀김 노이즈(전체 길이의 앞 6%에만).
                // 여기에도 같은 어택을 곱한다 - 곱하지 않으면 첫 샘플이 0이 아니어서(노이즈는
                // 엔벨로프가 없다) 재생 시작 순간에 딱 하는 클릭이 난다(실측: 첫 샘플 0.068).
                float white = (float)random.NextDouble() * 2f - 1f;
                noiseLp += (white - noiseLp) * 0.4f;
                float splash = p < 0.06f ? noiseLp * (1f - p / 0.06f) * 0.35f * attack : 0f;

                samples[i] = body * 0.8f + splash;
            }

            // 마지막 3ms를 0으로 눕혀 원샷 끝의 클릭을 막는다(지수 감쇠만으로는 정확히 0이 아니다).
            int tail = Mathf.Min(sampleCount, Mathf.RoundToInt(SampleRate * 0.003f));
            for (int i = 0; i < tail; i++)
                samples[sampleCount - 1 - i] *= i / (float)tail;

            NormalizePeak(samples, 0.45f);
            return BuildClip($"WaterDrip_{baseFrequency}", samples);
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
