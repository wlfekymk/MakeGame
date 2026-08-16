using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 설치형 구조물(물 증류기, 쉼터 등)의 시각적 파츠를 절차적으로 만들어주는 공용 유틸리티.
    /// 프리미티브 하나짜리 밋밋한 플레이스홀더 대신, 여러 프리미티브를 조합해 형태를 갖추게 하는 데 쓴다.
    ///
    /// B4(오브젝트 실루엣 정리)에서 역할이 하나 더 늘었다: 이 프로젝트의 모든 프리미티브 표면이
    /// CreateColorMaterial 한 곳을 거치므로, "월드 오브젝트가 쓰는 색"의 단일 소스도 여기에 둔다.
    /// EffectBuilder가 파티클 쪽 팔레트를 상수로 들고 있는 것과 정확히 같은 구조이며, 값은
    /// Docs/ArtDirection.md 1장의 hex를 그대로 옮긴 것이다(새 색을 만들지 않는다).
    /// </summary>
    public static class StructureVisualBuilder
    {
        // ── 월드 오브젝트 팔레트 (Docs/ArtDirection.md 1.1 / 1.2) ─────────────────
        /// <summary>Island Sand #C2B280 — 지형 기본색.</summary>
        public static readonly Color IslandSand = new Color(0.761f, 0.698f, 0.502f);

        /// <summary>Driftwood #8C6640 — 나뭇가지·대나무 등 목재 계열 전체.</summary>
        public static readonly Color Driftwood = new Color(0.549f, 0.400f, 0.251f);

        /// <summary>Weathered Stone #808085 — 돌조각·부싯돌 등 석재 계열 전체.</summary>
        public static readonly Color WeatheredStone = new Color(0.502f, 0.502f, 0.522f);

        /// <summary>Salvage Metal #738094 — 금속조각·엔진부품 등 금속 계열 전체.</summary>
        public static readonly Color SalvageMetal = new Color(0.451f, 0.502f, 0.580f);

        /// <summary>Palm Fiber #948C4C — 수확한 마른 섬유(야자잎 아이템·천조각·노끈). 살아 있는 초목에는 쓰지 않는다.</summary>
        public static readonly Color PalmFiber = new Color(0.580f, 0.549f, 0.298f);

        /// <summary>
        /// Frond Green #6BA83F — 살아 있는 초목의 잎(야자잎·덤불). ArtDirection 1.1의 9번째 색.
        /// Medic Green(#4FA87A, 색상각 149°)과는 색상각이 95°로 54° 떨어져 있어 "치료·안전"의 청록 계열과
        /// 섞이지 않고, Palm Fiber(수확한 마른 섬유)와는 살아 있는 잎/죽은 섬유로 용도가 갈린다.
        /// </summary>
        public static readonly Color FrondGreen = new Color(0.420f, 0.659f, 0.247f);

        /// <summary>
        /// Meadow Green #8AA84F — 지면 풀(풀밭 캡·풀포기). ArtDirection 1.1의 10번째 색.
        /// Frond Green보다 노랗고 채도가 낮아(색상각 80°) 잎과 지면이 한 덩어리로 뭉치지 않으며,
        /// Island Sand(#C2B280, 색상각 45°)와는 색상각이 35° 벌어져 모래/풀 경계가 색으로 읽힌다.
        /// </summary>
        public static readonly Color MeadowGreen = new Color(0.541f, 0.659f, 0.310f);

        /// <summary>Danger Red #CC3333 — 위험 신호(상어 등지느러미와 동일한 용도).</summary>
        public static readonly Color DangerRed = new Color(0.800f, 0.200f, 0.200f);

        /// <summary>Supply Khaki #597366 — 표류 보급품(부력통/비상식량/연료).</summary>
        public static readonly Color SupplyKhaki = new Color(0.349f, 0.451f, 0.400f);

        /// <summary>
        /// 표류물 표식용 밝은 미색. 팔레트 8색에는 밝은 색이 없어 야간(nightIntensity 0.10)과 모래
        /// (#C2B280) 위에서 동시에 대비가 서는 색이 하나도 없었다. 새로 만든 색이 아니라 이미
        /// IslandResourceSpawner가 비상식량 라벨에 쓰던 `Color.white * 0.9f` 관례를 상수로 승격한 것이다.
        /// "특별 자원(금속조각/부력통/엔진부품) 표식"이라는 단 하나의 용도로만 쓴다.
        /// </summary>
        public static readonly Color SalvageMarkerWhite = new Color(0.902f, 0.894f, 0.855f);

        /// <summary>
        /// 프리미티브 표면 기본 광택. URP Lit 기본값(0.5)은 텍스처가 노이즈 그레인 한 장뿐인 이
        /// 프로젝트에서 모든 오브젝트를 반들거리는 플라스틱처럼 보이게 만든다(모래·나무·잎·천은 전부
        /// 무광 재질이다). 로우폴리/스타일라이즈드 방향(ArtDirection 0장)에 맞춰 무광으로 낮춘다.
        /// </summary>
        public const float DefaultSmoothness = 0.15f;

        /// <summary>
        /// 지정한 프리미티브로 순수 시각용 파츠를 만들어 parent의 자식으로 붙인다.
        /// 자동으로 생성되는 콜라이더는 제거해 부모의 상호작용용 콜라이더와 중복/간섭되지 않게 한다.
        /// textureName을 주면 그 표면 질감 텍스처(Resources/Textures/*)를 씌운다(비우면 noise).
        /// </summary>
        public static GameObject CreateVisualPart(Transform parent, string name, PrimitiveType primitiveType,
            Vector3 localPosition, Vector3 localScale, Color color, Quaternion? localRotation = null,
            string textureName = null)
        {
            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            go.transform.localScale = localScale;

            // 시각 전용 파츠이므로 프리미티브 생성 시 자동으로 붙는 콜라이더는 제거한다.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = CreateColorMaterial(color, textureName);

            return go;
        }

        /// <summary>
        /// 위 CreateVisualPart와 동작이 같지만, 머티리얼을 새로 만들지 않고 **호출자가 만들어 둔 것을 공유**한다.
        ///
        /// 왜 필요한가: Color 오버로드는 파츠 하나당 CreateColorMaterial을 한 번씩 부르므로 파츠 수만큼
        /// 머티리얼 인스턴스가 생긴다. 구조물 하나가 파츠 5~10개일 때는 문제가 없었지만, 뗏목(RaftStructure)은
        /// 완성 단계에서 파츠가 40개를 넘고 건조 단계가 바뀔 때마다 통째로 다시 만든다 - 그대로 두면 한
        /// 오브젝트가 머티리얼을 수십 개씩 계속 새로 뱉어 SRP 배처가 죽는다(AGENT_BRIEF 4장 "머티리얼을
        /// 파츠마다 만들지 마라"). 색/텍스처가 같은 파츠들이 머티리얼 하나를 공유하도록 하는 통로다.
        ///
        /// Color 오버로드와 시그니처가 겹치지 않는다(Color는 구조체, Material은 클래스라 서로 암시적
        /// 변환이 없다) - 기존 호출부는 한 곳도 영향을 받지 않는다.
        /// </summary>
        public static GameObject CreateVisualPart(Transform parent, string name, PrimitiveType primitiveType,
            Vector3 localPosition, Vector3 localScale, Material sharedMaterial, Quaternion? localRotation = null)
        {
            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            go.transform.localScale = localScale;

            // 시각 전용 파츠이므로 프리미티브 생성 시 자동으로 붙는 콜라이더는 제거한다(Color 오버로드와 동일).
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && sharedMaterial != null)
                renderer.sharedMaterial = sharedMaterial;

            return go;
        }

        /// <summary>
        /// 지정한 단색의 기본 URP Lit 머티리얼을 만든다 (섬 지형 생성 시 사용한 것과 동일한 방식).
        /// 이 게임의 모든 프리미티브 기반 시각 파츠(쉼터/물증류기/모닥불/사냥감/위험요소 등)가
        /// 전부 이 메서드를 거치므로, 여기서 절차적 그레인 텍스처를 함께 곱해 씌우면
        /// 프로젝트 전체의 밋밋한 단색 프리미티브 문제를 한 번에 개선할 수 있다.
        /// textureName을 주면 그 텍스처를(wood/stone/metal/leaf/sand/water 등), 비우면 기존과 동일하게
        /// noise를 쓴다 - 기존 호출부는 인자를 넘기지 않으므로 동작이 100% 그대로 유지된다.
        /// </summary>
        public static Material CreateColorMaterial(Color color, string textureName = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = color;

            // 무광 처리: 프로퍼티가 없는 셰이더(Standard 폴백 등)에서도 경고 없이 넘어가도록 가드한다.
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", DefaultSmoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", DefaultSmoothness);

            // 흑백 얼룩 노이즈 텍스처를 곱해 표면에 미세한 질감을 더한다 (색상은 그대로 위 material.color가 담당).
            string resolvedTexture = string.IsNullOrEmpty(textureName) ? "noise" : textureName;
            var surfaceTexture = Resources.Load<Texture2D>($"Textures/{resolvedTexture}");
            if (surfaceTexture != null)
            {
                material.mainTexture = surfaceTexture;
                material.mainTextureScale = new Vector2(1.5f, 1.5f);
            }
            return material;
        }

        /// <summary>
        /// "플레이어가 묶어 세운 기둥" 하나(사각 기둥 + 이음매의 밧줄 결속)를 만든다.
        ///
        /// 왜 필요한가(ArtDirection 2장 4번 규칙 - 자연물과 인공물의 시각 언어 분리): 프리미티브만
        /// 쓰는 이 프로젝트에서 자연물과 인공물을 가르는 가장 값싼 신호는 색이 아니라
        /// (a) 둥근 원기둥이 아닌 각진 사각 기둥, (b) 파츠가 만나는 지점의 결속(밧줄) 이 두 가지다.
        /// 현재 쉼터 다리·물증류기 지지대는 전부 매끈한 원기둥이라 야자수 줄기·대나무 자원과 같은
        /// 형태 언어를 쓰고 있어 "내가 지은 것"으로 읽히지 않는다.
        ///
        /// 파츠는 2개(기둥 + 결속 1개)로 묶었다 - 구조물 하나에 기둥이 4개까지 붙으므로 기둥당
        /// 파츠를 늘리면 비용이 곧바로 4배가 된다.
        /// 호출부 연결은 이 클래스 소유가 아닌 Shelter.cs/WaterStill.cs 쪽 몫이라 여기서는 만들어만 둔다
        /// (CreatureVisualBuilder가 HazardSpawner와 연결될 때 쓴 것과 동일한 절차).
        /// </summary>
        public static GameObject CreateLashedPost(Transform parent, string name, Vector3 localPosition,
            float height, float thickness, Color woodColor)
        {
            var post = CreateVisualPart(parent, name, PrimitiveType.Cube, localPosition,
                new Vector3(thickness, height, thickness), woodColor, null, "wood");

            // 결속(밧줄): 기둥 위쪽 이음매를 한 바퀴 감은 것처럼 살짝 두꺼운 띠를 두른다.
            // 회전을 주지 않는다 - 부모가 비균일 스케일일 때 회전한 자식은 전단(shear)으로 찌그러진다.
            CreateVisualPart(post.transform, "Lashing", PrimitiveType.Cube,
                new Vector3(0f, 0.34f, 0f), new Vector3(1.35f, 0.09f, 1.35f), PalmFiber, null, "leaf");

            return post;
        }
    }
}
