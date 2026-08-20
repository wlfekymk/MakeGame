using System.Collections.Generic;
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

        /// <summary>
        /// Bamboo Culm #B4BE64 — **살아 있는 대나무 줄기 전용**(자원 노드 대나무 · bamboo_a/b/c 모델).
        ///
        /// [B48] 대나무는 목재 계열이라는 이유로 Driftwood(#8C6640)를 쓰고 있었는데, Driftwood는
        /// "물에 밀려와 마른 나무"의 색이라 살아 있는 대나무가 마른 나뭇가지로 보였다(디렉터 지적).
        /// 실제 대나무 줄기는 갈색이 아니라 **황록색**이다. Driftwood를 고치지 않고 새 색을 더하는
        /// 이유는 나뭇가지·표류물·궤짝이 같은 상수를 함께 쓰고 있어서다(그쪽은 마른 나무가 맞다).
        ///
        /// 팔레트 정합: 색상각 68°로 Meadow Green(80°)·Frond Green(95°)과 같은 황–녹 대역에 있으면서
        /// 명도가 더 높아(상대휘도 약 180 vs 잎 147) 잎(Frond Green)을 앞에 세워도 줄기가 뒤로 물러난다.
        /// Palm Fiber(#948C4C, 수확한 마른 섬유)와는 명도·채도가 함께 벌어져 아이템/월드가 섞이지 않는다.
        /// </summary>
        public static readonly Color BambooCulm = new Color(0.706f, 0.745f, 0.392f);

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
        /// <summary>
        /// 런타임 생성 머티리얼 이름 접두어. 에셋(내장 Default-Material 등)과 구분하는 유일한 단서다.
        /// </summary>
        public const string RuntimeMaterialPrefix = "MG~";

        public static Material CreateColorMaterial(Color color, string textureName = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = color;

            // [B29 감독] 런타임에 우리가 만든 머티리얼임을 이름에 새긴다.
            // 이게 없으면 나중에 이 머티리얼을 교체하는 쪽이 "지워도 되는 인스턴스"와
            // "지우면 안 되는 내장 에셋"(GameObject.CreatePrimitive가 붙여 주는 Default-Material)을
            // 구분할 방법이 런타임에 없다 - 실제로 그 구분 실패로 콘솔에
            // "Destroying assets is not permitted to avoid data loss"가 54번 찍혔다.
            material.name = RuntimeMaterialPrefix + material.name;

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

            // [실사감 E1] 비에 젖는 명부에 올린다. 이 메서드가 이 게임의 모든 프리미티브 시각 파츠가
            // 거치는 단 하나의 길목이므로, 여기 한 줄이면 오두막·바위·통나무·뗏목 갑판이 전부 젖는다.
            //
            // ★ 반드시 **색과 매끈함을 다 정한 뒤**에 부를 것. 등록 시점의 값이 "마른 값"으로
            //   저장되고, 비가 그치면 정확히 그 값으로 되돌아간다. 값을 정하기 전에 부르면
            //   덜 만들어진 상태가 마른 값으로 굳는다.
            MakeGame.Systems.SurfaceWetness.Register(material);

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

        /// <summary>
        /// [B29] 절차 메시 하나를 순수 시각 파츠로 붙인다(콜라이더가 한 프레임도 생기지 않는 경로).
        ///
        /// CreateVisualPart는 GameObject.CreatePrimitive로 만든 뒤 콜라이더를 Object.Destroy하는데,
        /// Destroy는 프레임 끝까지 지연되므로 그 사이에 다른 스포너의 SnapToGround 레이가 스칠 수 있다
        /// (IslandMeshGenerator.CreatePart가 초목에서 같은 이유로 같은 경로를 쓴다). 이 메서드는
        /// 프리미티브를 거치지 않으므로 그 위험이 원리적으로 없다.
        ///
        /// 머티리얼은 **호출자가 공유 캐시에서 받아온 것**을 그대로 쓴다
        /// (ResourceVisualLibrary.GetMaterial - 색+텍스처 조합당 하나). 여기서 새로 만들지 않는다.
        /// </summary>
        public static GameObject CreateMeshPart(Transform parent, string name, Mesh mesh,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material sharedMaterial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            if (mesh != null)
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            if (sharedMaterial != null)
                renderer.sharedMaterial = sharedMaterial;

            return go;
        }
    }

    /// <summary>
    /// [B29] 월드 장식물(바위·표류물·덤불·비행기 잔해)이 공유하는 **절차 메시 조립기**.
    ///
    /// 왜 이게 필요한가: 이 프로젝트에는 3D 모델 에셋이 0개라 형태를 전부 런타임에 만든다. 디테일을
    /// 프리미티브 파츠로 덧붙이면 드로우콜이 파츠 수만큼 늘어나므로, 직전 배치(B28, 자원 노드)에서
    /// 확립한 원칙 - **디테일은 파츠가 아니라 메시 안에 굽는다** - 를 장식물에도 그대로 적용한다.
    /// 바위의 각진 균열면, 통의 테, 상자의 널판 홈, 덤불의 잎끝은 전부 파츠가 아니라 정점이다.
    ///
    /// ResourceVisualLibrary(IslandResourceSpawner.cs)의 private MeshBuilder와 같은 규칙을 따르지만,
    /// 그쪽은 자원 노드 전용 private 클래스라 밖에서 쓸 수 없어 여기에 공개판을 둔다. 규칙 3가지:
    ///  1. 감김(winding)을 표로 외우지 않는다. 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 옮기면
    ///     통째로 안쪽을 향해 컬링되는 사고가 반복됐다 - 삼각형마다 기하 법선을 기준 방향과 맞춘다.
    ///  2. 평면 셰이딩(정점 비공유)이 기본이다. 로우폴리에서 각이 서야 "깎인 돌"로 읽힌다.
    ///  3. 메시 규격은 호출부와 **문서로 합의**한다. 이 클래스가 만드는 덩어리(AddChunk/Chunk)는
    ///     항상 요청한 size를 정확히 채우므로, 호출부는 "미터"를 그대로 넣으면 된다.
    ///     (과거 사고: 메시 형태만 바꾸고 호출부 스케일을 그대로 둬서 거대한 판이 된 적이 있다.)
    /// </summary>
    public class WorldMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        /// <summary>정이십면체를 소분할한 방향 벡터 목록(면당 3개). 분할 단계별로 한 번만 계산해 캐시한다.</summary>
        private static readonly Dictionary<int, Vector3[]> icosphereCache = new Dictionary<int, Vector3[]>();

        /// <summary>
        /// 각진 덩어리 하나를 메시에 더한다(바위·돌 파편·덤불 로브 공용).
        ///
        /// 정이십면체를 subdivisions회 소분할한 뒤, 방향만 보고 결정되는 연속 함수로 각 정점의 반지름을
        /// 흔든다. 이웃 삼각형이 같은 방향에서 같은 반지름을 받으므로 틈이 절대 생기지 않으면서
        /// (난수 표집이 아니라 함수라는 것이 핵심이다) 면마다 각이 서서 "쪼개진 바위"로 읽힌다.
        /// 마지막에 **축마다 따로** 정규화해, 각 축의 최대 반지름이 정확히 size/2가 되게 한다
        /// (= 덩어리가 size 상자를 절대 넘지 않고, 세 축 모두 한 번씩은 상자 면에 닿는다).
        /// 균일 배율로 하면 꼭짓점 흔들림 때문에 가로가 세로의 1.9배까지 커져 호출부가 지정한 크기와
        /// 어긋난다 - 자원 노드 쪽(ResourceVisualLibrary.BuildAngularChunk)이 같은 이유로 같은 처리를 한다.
        /// 즉 size는 미터 단위의 실제 크기이고, 호출부가 스케일을 따로 곱할 필요가 없다.
        /// </summary>
        /// <param name="center">덩어리 중심(메시 로컬).</param>
        /// <param name="size">덩어리를 가두는 상자의 크기(가로·세로·깊이). 이 상자를 넘지 않는다.</param>
        /// <param name="seed">모양 시드. 같은 값이면 항상 같은 모양이다(UnityEngine.Random을 쓰지 않는다).</param>
        /// <param name="jitter">반지름 흔들림 폭(0~0.8). 클수록 울퉁불퉁하다.</param>
        /// <param name="subdivisions">0이면 20면, 1이면 80면, 2면 320면. 큰 바위일수록 올린다.</param>
        public void AddChunk(Vector3 center, Vector3 size, int seed, float jitter, int subdivisions)
        {
            Vector3[] directions = GetIcosphere(subdivisions);
            var points = new Vector3[directions.Length];

            float maxX = 0.0001f;
            float maxY = 0.0001f;
            float maxZ = 0.0001f;
            for (int i = 0; i < directions.Length; i++)
            {
                points[i] = directions[i] * ChunkRadius(directions[i], seed, jitter);
                maxX = Mathf.Max(maxX, Mathf.Abs(points[i].x));
                maxY = Mathf.Max(maxY, Mathf.Abs(points[i].y));
                maxZ = Mathf.Max(maxZ, Mathf.Abs(points[i].z));
            }

            var scale = new Vector3(size.x * 0.5f / maxX, size.y * 0.5f / maxY, size.z * 0.5f / maxZ);
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3(points[i].x * scale.x, points[i].y * scale.y, points[i].z * scale.z);

            for (int f = 0; f + 2 < points.Length; f += 3)
            {
                Vector3 a = center + points[f];
                Vector3 b = center + points[f + 1];
                Vector3 c = center + points[f + 2];
                // 반지름이 항상 양수라 덩어리는 center에 대해 별모양(star-shaped)이다
                // = 무게중심 방향이 곧 바깥 방향이다.
                AddFace(a, b, c, (a + b + c) / 3f - center);
            }
        }

        /// <summary>AddChunk 하나짜리 메시를 바로 만들어 준다(공유 캐시에 넣어 두고 쓸 것).</summary>
        public static Mesh Chunk(string name, Vector3 size, int seed, float jitter, int subdivisions)
        {
            var builder = new WorldMeshBuilder();
            builder.AddChunk(Vector3.zero, size, seed, jitter, subdivisions);
            return builder.Finish(name);
        }

        /// <summary>
        /// 방향만으로 결정되는 반지름(1 ± jitter). 사인 4개를 겹쳐 저주파 굴곡 + 잔주름을 만든다.
        /// 난수를 쓰지 않으므로 재현성 규칙(AGENT_BRIEF 2장 6번)에 걸리지 않고, 같은 seed면 어떤
        /// 분할 단계에서도 같은 실루엣이 나온다.
        /// </summary>
        private static float ChunkRadius(Vector3 direction, int seed, float jitter)
        {
            float s = seed * 0.6180339f;
            float n = Mathf.Sin(direction.x * 4.7f + s) * 0.50f
                + Mathf.Sin(direction.y * 5.9f + s * 1.7f) * 0.34f
                + Mathf.Sin(direction.z * 6.7f + s * 2.3f) * 0.42f
                + Mathf.Sin((direction.x + direction.y + direction.z) * 9.1f + s * 3.1f) * 0.20f;
            // n의 이론 최대는 1.46이라, jitter 0.8에서도 반지름이 0 아래로 내려가지 않는다(별모양 유지).
            return 1f + Mathf.Clamp(jitter, 0f, 0.8f) * n * 0.68f;
        }

        /// <summary>정이십면체를 subdivisions회 소분할한 단위구 방향(면당 정점 3개 나열).</summary>
        private static Vector3[] GetIcosphere(int subdivisions)
        {
            int level = Mathf.Clamp(subdivisions, 0, 2);
            Vector3[] cached;
            if (icosphereCache.TryGetValue(level, out cached) && cached != null)
                return cached;

            const float phi = 1.618034f;
            var basePoints = new[]
            {
                new Vector3(-1f, phi, 0f), new Vector3(1f, phi, 0f), new Vector3(-1f, -phi, 0f), new Vector3(1f, -phi, 0f),
                new Vector3(0f, -1f, phi), new Vector3(0f, 1f, phi), new Vector3(0f, -1f, -phi), new Vector3(0f, 1f, -phi),
                new Vector3(phi, 0f, -1f), new Vector3(phi, 0f, 1f), new Vector3(-phi, 0f, -1f), new Vector3(-phi, 0f, 1f),
            };
            int[] faces =
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
            };

            var current = new List<Vector3>(faces.Length);
            for (int i = 0; i < faces.Length; i++)
                current.Add(basePoints[faces[i]].normalized);

            for (int s = 0; s < level; s++)
            {
                var next = new List<Vector3>(current.Count * 4);
                for (int f = 0; f + 2 < current.Count; f += 3)
                {
                    Vector3 a = current[f];
                    Vector3 b = current[f + 1];
                    Vector3 c = current[f + 2];
                    Vector3 ab = ((a + b) * 0.5f).normalized;
                    Vector3 bc = ((b + c) * 0.5f).normalized;
                    Vector3 ca = ((c + a) * 0.5f).normalized;

                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(ab); next.Add(b); next.Add(bc);
                    next.Add(ca); next.Add(bc); next.Add(c);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }
                current = next;
            }

            Vector3[] result = current.ToArray();
            icosphereCache[level] = result;
            return result;
        }

        /// <summary>
        /// 회전한 직육면체 하나(6면 12삼각형)를 메시에 더한다. 상자를 파츠로 붙이는 대신 메시에
        /// 굽기 위한 것이다 - 상자 5개짜리 궤짝도 렌더러는 1개가 된다.
        /// </summary>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 hx = rotation * new Vector3(size.x * 0.5f, 0f, 0f);
            Vector3 hy = rotation * new Vector3(0f, size.y * 0.5f, 0f);
            Vector3 hz = rotation * new Vector3(0f, 0f, size.z * 0.5f);

            AddQuad(center + hx + hy + hz, center + hx + hy - hz, center + hx - hy - hz, center + hx - hy + hz, hx, false);
            AddQuad(center - hx + hy + hz, center - hx + hy - hz, center - hx - hy - hz, center - hx - hy + hz, -hx, false);
            AddQuad(center + hy + hx + hz, center + hy + hx - hz, center + hy - hx - hz, center + hy - hx + hz, hy, false);
            AddQuad(center - hy + hx + hz, center - hy + hx - hz, center - hy - hx - hz, center - hy - hx + hz, -hy, false);
            AddQuad(center + hz + hx + hy, center + hz + hx - hy, center + hz - hx - hy, center + hz - hx + hy, hz, false);
            AddQuad(center - hz + hx + hy, center - hz + hx - hy, center - hz - hx - hy, center - hz - hx + hy, -hz, false);
        }

        /// <summary>중심선(centers)과 반지름(radii)을 따라가는 관을 하나 잇는다(굵기 변화가 곧 디테일이다).</summary>
        public void AddTube(Vector3[] centers, float[] radii, int sides, bool capStart, bool capEnd, float uvTile)
        {
            if (centers == null || radii == null || centers.Length < 2 || radii.Length != centers.Length || sides < 3)
                return;

            Vector3 axis = centers[centers.Length - 1] - centers[0];
            if (axis.sqrMagnitude < 0.0000001f)
                axis = Vector3.up;
            axis = axis.normalized;

            Vector3 helper = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(helper, axis).normalized;
            Vector3 forward = Vector3.Cross(axis, right);

            int start = vertices.Count;
            int stride = sides + 1; // 이음매에서 UV가 끊기도록 정점을 하나 겹친다
            for (int r = 0; r < centers.Length; r++)
            {
                for (int s = 0; s <= sides; s++)
                {
                    float angle = (float)s / sides * Mathf.PI * 2f;
                    Vector3 direction = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                    vertices.Add(centers[r] + direction * radii[r]);
                    uvs.Add(new Vector2((float)s / sides, (float)r / (centers.Length - 1) * uvTile));
                }
            }

            for (int r = 0; r + 1 < centers.Length; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a0 = start + r * stride + s;
                    int a1 = a0 + 1;
                    int b0 = a0 + stride;
                    int b1 = b0 + 1;
                    float mid = ((float)s + 0.5f) / sides * Mathf.PI * 2f;
                    Vector3 outward = right * Mathf.Cos(mid) + forward * Mathf.Sin(mid);
                    AddTriangle(a0, b0, b1, outward);
                    AddTriangle(a0, b1, a1, outward);
                }
            }

            if (capStart)
                AddCap(start, sides, centers[0], -axis);
            if (capEnd)
                AddCap(start + (centers.Length - 1) * stride, sides, centers[centers.Length - 1], axis);
        }

        /// <summary>사각면 하나. doubleSided면 감김을 뒤집은 사본을 함께 넣어 양쪽에서 보이게 한다(잎처럼 두께가 없는 면).</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference, bool doubleSided)
        {
            AddQuadFace(a, b, c, d, reference);
            if (doubleSided)
                AddQuadFace(a, b, c, d, -reference);
        }

        /// <summary>평면 셰이딩용 삼각면 하나(정점을 공유하지 않아 면마다 각이 선다).</summary>
        public void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 reference)
        {
            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            uvs.Add(new Vector2(a.x + 0.5f, a.z + 0.5f));
            uvs.Add(new Vector2(b.x + 0.5f, b.z + 0.5f));
            uvs.Add(new Vector2(c.x + 0.5f, c.z + 0.5f));
            AddTriangle(index, index + 1, index + 2, reference);
        }

        /// <summary>지금까지 넣은 면으로 메시를 마무리한다. 반드시 캐시해 두고 재사용할 것(파츠마다 새로 만들지 마라).</summary>
        public Mesh Finish(string name)
        {
            var mesh = new Mesh();
            mesh.name = name;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddQuadFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference)
        {
            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            AddTriangle(index, index + 1, index + 2, reference);
            AddTriangle(index, index + 2, index + 3, reference);
        }

        private void AddCap(int ringStart, int sides, Vector3 center, Vector3 reference)
        {
            int centerIndex = vertices.Count;
            vertices.Add(center);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s < sides; s++)
                AddTriangle(centerIndex, ringStart + s, ringStart + s + 1, reference);
        }

        /// <summary>삼각형 하나를 감김 방향까지 맞춰 넣는다. 기하 법선이 기준과 반대면 두 인덱스를 바꾼다.</summary>
        private void AddTriangle(int i0, int i1, int i2, Vector3 reference)
        {
            AddOrientedTriangle(vertices, triangles, i0, i1, i2, reference);
        }

        /// <summary>
        /// 감김 방향 보정 삼각형 추가의 **정본**(ResourceVisualLibrary/CreatureVisualBuilder의
        /// 메시 빌더도 이것을 호출한다). 기하 법선이 기준(reference)과 반대면 두 인덱스를 바꿔
        /// 넣는다 - 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 옮기면 통째로 안쪽을 향해
        /// 컬링되는 사고가 반복됐다(클래스 주석 규칙 1).
        /// </summary>
        internal static void AddOrientedTriangle(List<Vector3> vertices, List<int> triangles,
            int i0, int i1, int i2, Vector3 reference)
        {
            Vector3 geometric = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
            if (Vector3.Dot(geometric, reference) < 0f)
            {
                int swap = i1;
                i1 = i2;
                i2 = swap;
            }

            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
        }
    }
}
