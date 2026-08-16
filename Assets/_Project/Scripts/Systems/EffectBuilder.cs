using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 코드로만 파티클/이펙트를 만들어 쓰는 공용 유틸리티 (B4-11).
    ///
    /// 왜 이 클래스가 필요한가: 이 프로젝트에는 외부 3D/VFX 에셋이 하나도 없고, 파티클을 쓰는 곳은
    /// WeatherSystem(비)뿐이었다. 그 비 파티클 코드에 이미 "URP에서 파티클 머티리얼을 잘못 만들면
    /// 마젠타로 보인다"는 실측 검증 결과(Universal Render Pipeline/Particles/Unlit → Sprites/Default
    /// 순으로 찾기)가 녹아 있는데, 이펙트를 추가할 때마다 그 지식을 매번 베껴 쓰면 언젠가 반드시
    /// 한 곳이 어긋난다. 그래서 머티리얼 생성/셰이더 폴백/텍스처 생성을 이 한 곳으로 모았다.
    /// StructureVisualBuilder(구조물 프리미티브)·CreatureVisualBuilder(생물 프리미티브)와 완전히 같은
    /// 위치의 클래스 — 담당 영역만 "메시"가 아니라 "파티클"이다.
    ///
    /// 설계 규칙 (Docs/ArtDirection.md 준수):
    /// - 색은 팔레트 안에서만 고른다. 아래 상수 5개가 이 파일에서 쓰는 색 전부다.
    /// - 파티클 수는 시스템당 20~40개 이하로 묶는다(maxParticles). 무인도 한 곳에 모닥불이 여러 개
    ///   설치될 수 있고, 채집/피격 팝은 순간적으로 여러 개가 겹칠 수 있어 상한을 낮게 잡는다.
    /// - 일회성 이펙트(채집/피격)는 ParticleSystemStopAction.Destroy로 스스로 사라진다 — 오브젝트가
    ///   월드에 누적되지 않게 하는 유일한 안전장치라 반드시 유지할 것.
    /// - 4.2 피드백 3단계 규칙: A단계(채집 등 일상 행동)는 "화면 전체 이펙트 금지"이지 월드 공간의
    ///   국소적 신호까지 금지하는 규칙이 아니다. 채집 팝은 자원 노드 위치에만 뜨는 월드 이펙트라
    ///   A단계 규칙과 충돌하지 않는다. C단계(피격)의 화면 테두리 플래시는 기존 CombatFeedbackUI가
    ///   그대로 담당하고, 여기서는 "어디서 맞았는지"를 알려주는 월드 공간 신호만 더한다.
    /// </summary>
    public static class EffectBuilder
    {
        // ── 팔레트 (Docs/ArtDirection.md 1장) ─────────────────────────────
        /// <summary>Food Orange #D98C33 — 불꽃 본체 / 모닥불 불빛.</summary>
        public static readonly Color FoodOrange = new Color(0.851f, 0.549f, 0.200f);

        /// <summary>Sunstroke Gold #E6BF33 — 불꽃 심지(가장 뜨거운 부분).</summary>
        public static readonly Color SunstrokeGold = new Color(0.902f, 0.749f, 0.200f);

        /// <summary>Neutral Gray #CCCCCC — 연기.</summary>
        public static readonly Color NeutralGray = new Color(0.800f, 0.800f, 0.800f);

        /// <summary>Danger Red #CC3333 — 피격/출혈.</summary>
        public static readonly Color DangerRed = new Color(0.800f, 0.200f, 0.200f);

        /// <summary>Palm Fiber #948C4C — 자원 색을 못 읽었을 때 채집 팝의 기본색.</summary>
        public static readonly Color PalmFiber = new Color(0.580f, 0.549f, 0.298f);

        private static Material cachedParticleMaterial;
        private static Texture2D cachedSoftDotTexture;

        /// <summary>
        /// 모든 파티클이 공유하는 머티리얼 하나를 만들어 캐시한다. 입자별 색은 머티리얼이 아니라
        /// ParticleSystem의 startColor/colorOverLifetime(정점 색)이 결정하므로, 색이 달라도 머티리얼은
        /// 하나면 충분하다 — 이펙트마다 new Material()을 만들면 드로우콜과 메모리가 그만큼 늘어난다.
        /// 셰이더 탐색 순서는 WeatherSystem이 실측으로 검증한 것과 동일하다(URP 전용 → Sprites/Default).
        /// HideAndDontSave를 줘서 씬 전환 시 UnloadUnusedAssets에 쓸려나가지 않게 한다.
        /// </summary>
        public static Material GetParticleMaterial()
        {
            if (cachedParticleMaterial != null)
                return cachedParticleMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default"); // URP에서도 안전하게 동작하는 대체 셰이더

            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.hideFlags = HideFlags.HideAndDontSave;

            Texture2D dot = GetSoftDotTexture();
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", dot);
            material.mainTexture = dot;

            // URP Lit/Unlit 계열은 기본이 Opaque라, 이대로 두면 입자의 알파(부드러운 가장자리·페이드
            // 아웃)가 전부 무시되고 네모난 판때기로 보인다. 런타임에서 투명 모드로 전환하려면 아래
            // 프로퍼티/키워드를 직접 세팅해야 한다. Sprites/Default로 폴백된 경우엔 해당 프로퍼티가
            // 없으므로 HasProperty로 걸러 경고 없이 건너뛴다(그 셰이더는 이미 투명이라 설정 불필요).
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f); // Alpha 블렌드
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            cachedParticleMaterial = material;
            return cachedParticleMaterial;
        }

        /// <summary>
        /// 가장자리가 부드럽게 사라지는 흰 원형 점 텍스처(32x32)를 코드로 만든다.
        /// 텍스처가 없으면 파티클이 딱딱한 정사각형으로 보여 불꽃/연기가 픽셀 덩어리처럼 읽히는데,
        /// 이 프로젝트는 외부 에셋 의존이 금지라 파일을 가져올 수 없으므로 절차적으로 굽는다.
        /// 32x32(1024픽셀) 한 장을 전 이펙트가 공유하므로 비용은 사실상 0이다.
        /// </summary>
        private static Texture2D GetSoftDotTexture()
        {
            if (cachedSoftDotTexture != null)
                return cachedSoftDotTexture;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha; // 제곱해서 중심은 진하게, 가장자리는 더 빠르게 사라지게
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            cachedSoftDotTexture = texture;
            return cachedSoftDotTexture;
        }

        /// <summary>
        /// 파티클 시스템을 담을 자식 오브젝트를 만들고 공용 머티리얼까지 물려준다.
        /// localRotation을 (-90,0,0)으로 눕히는 이유: 코드로 AddComponent한 ParticleSystem은 로컬 +Z
        /// 방향으로 입자를 뿜는데(에디터 메뉴로 만든 파티클이 위로 솟는 건 그 오브젝트가 애초에
        /// X -90도로 만들어지기 때문), 그대로 두면 불꽃이 하늘이 아니라 앞으로 뿜어져 나간다.
        /// </summary>
        private static ParticleSystem CreateSystem(string name, Transform parent, Vector3 localPosition, bool pointUpward)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = pointUpward ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetParticleMaterial();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
            }

            return ps;
        }

        /// <summary>
        /// 모닥불 불꽃. 입자마다 Sunstroke Gold(심지)와 Food Orange(겉불꽃) 사이에서 색을 무작위로
        /// 골라 두 팔레트 색이 섞여 타오르게 하고, 수명 끝에서 알파 0으로 사라진다. 단색 하나로만
        /// 뿜으면 주황 스티커처럼 평평해 보여서, 색을 두 개 섞는 것이 가장 싼 입체감 확보 수단이다.
        /// 우선순위 1번(가장 눈에 띄어야 하는 안전지대 신호)이지만
        /// maxParticles는 30으로 묶었다 — 불꽃은 "많이"보다 "빠르게 흔들리는가"로 읽히기 때문에
        /// 입자 수를 늘리는 것보다 수명을 짧게(0.5~0.9초) 가져가는 쪽이 같은 비용에서 훨씬 잘 보인다.
        /// </summary>
        public static ParticleSystem CreateCampfireFlame(Transform parent, Vector3 localPosition)
        {
            ParticleSystem ps = CreateSystem("CampfireFlame", parent, localPosition, true);

            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.30f);
            main.startColor = new ParticleSystem.MinMaxGradient(SunstrokeGold, FoodOrange);
            main.gravityModifier = -0.15f; // 살짝 떠오르게 (뜨거운 공기)
            main.maxParticles = 30;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 22f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.18f;

            ApplyFadeOut(ps, 1f);
            ApplyShrinkOverLifetime(ps);

            return ps;
        }

        /// <summary>
        /// 모닥불 연기. 불꽃보다 느리고 크고 흐리게, 더 높이 올라간다. 불꽃만 있으면 멀리서는 작은
        /// 주황 점 하나라 눈에 안 띄는데, 연기 기둥은 높이가 있어 "저기 안전지대가 있다"는 신호를
        /// 지형 너머 먼 거리에서도 전달한다 — 실제로 이 이펙트의 존재 이유가 그 원거리 가독성이다.
        /// 색은 팔레트의 Neutral Gray를 알파 낮게 쓴다(팔레트 밖 색을 새로 만들지 않기 위함).
        /// </summary>
        public static ParticleSystem CreateCampfireSmoke(Transform parent, Vector3 localPosition)
        {
            ParticleSystem ps = CreateSystem("CampfireSmoke", parent, localPosition, true);

            var main = ps.main;
            main.loop = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startColor = new Color(NeutralGray.r, NeutralGray.g, NeutralGray.b, 0.35f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 20;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.12f;

            // 연기의 흐릿함(알파 0.35)은 startColor가 이미 담당하므로 여기서는 1f로 두고 페이드만 건다.
            ApplyFadeOut(ps, 1f);

            // 연기는 위로 갈수록 퍼져야 기둥처럼 읽힌다(불꽃과 반대로 커진다).
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));

            return ps;
        }

        /// <summary>
        /// 채집 성공 팝(우선순위 2번). 자원 노드의 실제 표면 색을 그대로 뽑아 써서 "무엇을 얻었는지"가
        /// 색으로 읽히게 한다 — 노드 색은 이미 MaterialFamily/팔레트를 거쳐 칠해져 있으므로, 여기서
        /// 색을 새로 정하면 오히려 팔레트를 벗어난다. 색을 못 읽으면 Palm Fiber로 폴백한다.
        /// 14개 짧은 버스트 한 번이라 A단계(일상 행동)의 가벼움을 넘지 않는다.
        /// </summary>
        public static void PlayHarvestPop(GameObject node)
        {
            if (node == null)
                return;

            Vector3 position = node.transform.position;
            Color tint = PalmFiber;

            var renderer = node.GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
            {
                position = renderer.bounds.center + Vector3.up * (renderer.bounds.extents.y * 0.6f);
                if (renderer.sharedMaterial != null)
                    tint = renderer.sharedMaterial.color;
            }

            PlayOneShotBurst("HarvestPop", position, tint, 14, 0.45f, 1.2f, 2.2f, 0.05f, 0.11f, 1.4f);
        }

        /// <summary>
        /// 피격/출혈 팝(우선순위 3번). 위험 요소와 접촉한 그 순간, 맞은 지점 주변에 Danger Red 입자를
        /// 짧게 튀긴다. 기존 CombatFeedbackUI의 화면 테두리 플래시(2D)는 "맞았다"만 알려줄 뿐 방향이나
        /// 위치 정보가 없어서, 어디에 있는 무엇에게 맞았는지 모른 채 계속 맞는 상황이 나온다.
        /// 월드 공간 파티클은 그 공백만 메운다 — 화면 전체 이펙트를 더하는 게 아니므로 4.2의
        /// "상시 피해에는 화면 플래시 금지" 규칙과 충돌하지 않는다(호출부도 접촉 진입점 한 곳뿐).
        /// </summary>
        public static void PlayHitBurst(Vector3 worldPosition)
        {
            PlayOneShotBurst("HitBurst", worldPosition, DangerRed, 18, 0.55f, 1.6f, 3.0f, 0.06f, 0.14f, 2.0f);
        }

        /// <summary>
        /// 한 번 터지고 스스로 사라지는 구형 버스트 파티클의 공통 구현.
        /// 구(Sphere) 셰이프라 에미터 회전과 무관하게 사방으로 퍼지고, 중력으로 아래로 떨어지며 사라진다.
        /// stopAction=Destroy 덕분에 호출자가 뒷정리를 신경 쓸 필요가 없다(오브젝트 누적 방지의 핵심).
        /// </summary>
        private static void PlayOneShotBurst(string name, Vector3 worldPosition, Color color, int count,
            float lifetime, float minSpeed, float maxSpeed, float minSize, float maxSize, float gravity)
        {
            ParticleSystem ps = CreateSystem(name, null, Vector3.zero, false);
            ps.transform.position = worldPosition;

            var main = ps.main;
            main.loop = false;
            main.duration = lifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = Mathf.Max(count, 8);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy; // 다 타고 나면 오브젝트째 자동 소멸

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            ApplyFadeOut(ps, 1f);
            ApplyShrinkOverLifetime(ps);

            ps.Play();
        }

        /// <summary>
        /// 수명이 끝날수록 알파가 0으로 빠지게 한다. 페이드가 없으면 입자가 수명 끝에 갑자기 툭
        /// 사라져 눈에 거슬린다.
        /// 색 키를 흰색으로 고정한 것이 중요하다: colorOverLifetime은 startColor를 "대체"하는 게 아니라
        /// "곱한다". 여기에 팔레트 색을 한 번 더 넣으면 주황 × 주황 = 팔레트에 없는 어두운 갈색이
        /// 되어버린다. 흰색(1,1,1)을 곱하면 startColor에 지정한 팔레트 색이 그대로 화면에 나온다.
        /// </summary>
        private static void ApplyFadeOut(ParticleSystem ps, float startAlpha)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(startAlpha, 0f),
                    new GradientAlphaKey(startAlpha * 0.8f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });

            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>
        /// [B22] 빗방울이 지면/수면에 부딪혀 퍼지는 물튀김(파문). WeatherSystem이 비가 오는 동안
        /// 플레이어 발밑 지면 높이에 붙여 두고, 방출량을 강우 세기에 비례시켜 쓴다.
        ///
        /// 왜 이 이펙트가 필요한가: 기존 비는 카메라 위에서 떨어지는 빗줄기 하나뿐이라, 화면 앞에
        /// 붙은 레이어처럼 보이고 **월드에 닿지 않았다**. 지면에 닿는 신호가 하나 생기면 같은
        /// 빗줄기가 갑자기 "이 섬에 내리는 비"로 읽힌다 - 파티클 하나 추가로 얻는 것 치고 효과가 크다.
        ///
        /// 비용: 시스템 1개(섬 개수와 무관한 단일 인스턴스), maxParticles 60, 삼각형 120.
        /// HorizontalBillboard라 입자가 지면에 납작하게 눕는다(View 정렬이면 공중에 뜬 점으로 보인다).
        /// useUnscaledTime: 엔딩/사망으로 timeScale이 0이 되어도 얼어붙지 않게 한다(AGENT_BRIEF 4장).
        /// </summary>
        public static ParticleSystem CreateRainSplashes(Transform parent)
        {
            // pointUpward=false: 아래 shape을 Box로 덮어쓰고 방출 방향도 안 쓰므로 회전이 필요 없다.
            ParticleSystem ps = CreateSystem("RainSplashes", parent, Vector3.zero, false);

            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
            main.startSpeed = 0f;                       // 파문은 번지기만 하고 이동하지 않는다
            main.gravityModifier = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            // 알파는 아래 ApplyFadeOut의 시작 알파(0.5)가 단독으로 정하게 1로 둔다.
            // colorOverLifetime은 startColor를 "곱하기" 때문에 양쪽에 0.45를 넣으면 0.2가 되어
            // 화면에서 사실상 보이지 않는다(이 파일 ApplyFadeOut 주석의 함정과 같은 것).
            main.startColor = new Color(0.78f, 0.86f, 0.95f, 1f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.rateOverTime = 0f;                 // WeatherSystem이 강우 세기에 맞춰 채운다

            // 플레이어 주변 12m만 덮는다. 더 넓히면 경사면에서 파문이 지면과 어긋나 떠 보인다
            // (섬 경사가 반지름 50m에 높이 8m = 0.16이라, 6m 밖이면 최대 1m 어긋난다).
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(12f, 0.01f, 12f);

            // 수명 동안 커지면서 흐려진다 = 물에 번지는 파문.
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f));

            ApplyFadeOut(ps, 0.5f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
                renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            return ps;
        }

        /// <summary>수명이 다할수록 입자가 작아지게 한다(불꽃이 사그라들고, 튄 조각이 잦아드는 느낌).</summary>
        private static void ApplyShrinkOverLifetime(ParticleSystem ps)
        {
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
        }
    }
}
