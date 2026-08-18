"""
mgbuild - MakeGame 절차 에셋 공용 라이브러리 (헤드리스 Blender)

Docs/AssetPipeline.md 의 계약을 **코드로 강제**하는 것이 이 파일의 목적이다.
계약을 어긴 메시는 조용히 통과하지 않고 ContractError 로 죽는다.

계약 요약 (AssetPipeline.md 1장):
  단위 미터 / 위 +Y / 정면 +Z / 오른쪽 +X / 밑면 y=0 / X,Z 중심 정렬 / 회전은 정점에 구움

쓰는 법 (units/*.py 에서):

    import mgbuild as mg
    mg.reset_scene()
    rng = mg.Rng(1234)
    obj = ...                       # bmesh 로 무언가 만든다
    stats = mg.enforce_contract(obj, tri_budget=4000, expect_size=(w, h, d))
    mg.export_obj(obj, "…/Models/foo.obj")
    mg.verify_obj_file("…/Models/foo.obj", stats)      # 내보낸 파일을 다시 읽어 대조
    mg.turntable(obj, "…/_preview/foo.png", title="foo", stats=stats)

의존: bpy(5.0.1) / numpy / PIL. Blender GUI 없이 `python3` 로 그냥 돈다.
"""

import math
import os
import random

import bpy  # bpy 를 먼저 import 해야 bmesh/mathutils 내장 모듈이 등록된다(순서 중요)
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

# ── 경로 ──────────────────────────────────────────────────────────────────────
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(TOOLS_DIR, "..", ".."))
MODELS_DIR = os.path.join(PROJECT_ROOT, "Assets", "_Project", "Resources", "Models")
TEXTURES_DIR = os.path.join(PROJECT_ROOT, "Assets", "_Project", "Resources", "Textures")
PREVIEW_DIR = os.path.join(TOOLS_DIR, "_preview")

# 계약 2장 삼각형 예산.
TRI_BUDGET = {
    "small_prop": 1500,
    "medium_prop": 4000,
    "large_structure": 8000,
    "creature": 12000,
    "hero": 20000,
}

EPS = 1e-5


class ContractError(Exception):
    """AssetPipeline.md 계약 위반. 절대 삼키지 마라 - 이게 이 파일의 존재 이유다."""


# ── 난수 ──────────────────────────────────────────────────────────────────────
class Rng:
    """시드 고정 난수. UnityEngine.Random 처럼 전역 상태에 의존하지 않는다.

    같은 시드 -> 같은 메시. 시드는 반드시 unit 스크립트에 **숫자로 적어 둔다**.
    """

    def __init__(self, seed):
        self.seed = int(seed)
        self._r = random.Random(self.seed)

    def uniform(self, a, b):
        return self._r.uniform(a, b)

    def randint(self, a, b):
        """[a, b] 양끝 포함."""
        return self._r.randint(a, b)

    def choice(self, seq):
        return self._r.choice(seq)

    def unit_vector(self):
        """구면 균등 분포 단위벡터."""
        z = self._r.uniform(-1.0, 1.0)
        t = self._r.uniform(0.0, math.tau)
        s = math.sqrt(max(0.0, 1.0 - z * z))
        return Vector((s * math.cos(t), z, s * math.sin(t)))

    def sub(self, salt):
        """같은 시드에서 갈라져 나온 독립 스트림(파츠별로 쓰면 편하다)."""
        return Rng(self.seed * 1000003 + int(salt))


# ── 씬 ────────────────────────────────────────────────────────────────────────
def reset_scene():
    """완전히 빈 씬에서 시작한다. 이전 실행의 오브젝트/메시/이미지가 남으면 재현성이 깨진다."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for coll in (bpy.data.meshes, bpy.data.objects, bpy.data.materials,
                 bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for item in list(coll):
            coll.remove(item, do_unlink=True)


def new_object(name, bm):
    """bmesh 를 씬 오브젝트로 굽는다(bmesh 는 여기서 free 된다)."""
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def triangulate(obj):
    """사각형/ngon 금지(계약 3장). 이 함수를 거치지 않은 메시는 내보내지 마라."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()


def shade_flat(obj):
    """로우폴리에서 각이 서야 '깎인 돌'로 읽힌다. 내보낼 때 면당 법선이 vn 으로 나간다."""
    for poly in obj.data.polygons:
        poly.use_smooth = False


def clean_bmesh(bm, dist=1e-4):
    """겹친 정점을 녹이고 퇴화(면적 0) 삼각형을 없앤다. 절단·불리언 뒤에는 항상 부른다.

    면적 0 삼각형은 RecalculateNormals 에서 법선이 0 이 되어 **그 면만 새까맣게** 보인다
    (게임 쪽 FrondMeters 주석이 같은 사고를 기록하고 있다). enforce_contract 가 이걸 막지만,
    막히기 전에 여기서 고치는 게 정상 경로다.
    """
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=dist)
    bmesh.ops.dissolve_degenerate(bm, dist=dist, edges=bm.edges[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    # remove_doubles 로도 안 사라지는 바늘 삼각형이 남을 수 있어 마지막에 한 번 더 본다.
    slivers = [f for f in bm.faces if f.calc_area() < 1e-9]
    if slivers:
        bmesh.ops.dissolve_faces(bm, faces=slivers, use_verts=True)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
    return bm


def decimate_to_budget(obj, target_tris, cleanup=True):
    """삼각형 수를 예산 안으로 줄인다(Decimate COLLAPSE). 이미 예산 안이면 아무것도 안 한다.

    UV 를 펴기 **전에** 부르는 것을 전제로 한다(감면이 UV 를 흔들지 않게).
    생성기를 예산에 맞춰 손으로 튜닝하는 것보다 이 쪽이 안전하다 - 예산은 계약이 정하고
    형태는 생성기가 정하게 분리된다.
    """
    tris = len(obj.data.polygons)
    if tris <= target_tris:
        return tris

    mod = obj.modifiers.new("MG_Decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.use_collapse_triangulate = True
    mod.ratio = max(0.02, (target_tris / tris) * 0.99)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)

    if cleanup:
        # 감면은 면적 0 삼각형을 남긴다 - 그대로 두면 법선이 0 이라 그 면만 새까맣게 보인다.
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges[:])
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(obj.data)
        bm.free()
    return len(obj.data.polygons)


def box_uv(obj, tile=1.0, layer_name="UVMap"):
    """박스(삼면) 투영 UV. 면의 법선이 가장 크게 향하는 축을 보고 나머지 두 축을 그대로 UV 로 쓴다.

    타일링 노이즈 텍스처(rock/bark/thatch 계열)에 쓰라고 만든 것이다. 이음새는 투영 축이
    바뀌는 모서리에만 생기고, 그 텍스처들은 방향성이 없어서 이음새가 눈에 띄지 않는다.
    UV 언랩 오퍼레이터를 쓰지 않으므로 headless 컨텍스트 문제도 없고 완전히 결정적이다.

    tile: 텍스처 한 장이 덮는 거리(미터). 0.9 면 0.9m 마다 반복된다.
    """
    mesh = obj.data
    uv = mesh.uv_layers.get(layer_name) or mesh.uv_layers.new(name=layer_name)
    inv = 1.0 / max(tile, EPS)
    for poly in mesh.polygons:
        n = poly.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            if ay >= ax and ay >= az:        # 위/아래 면 -> XZ 투영
                u, v = co.x, co.z if n.y >= 0 else -co.z
            elif ax >= az:                   # 좌/우 면 -> ZY 투영
                u, v = (-co.z if n.x >= 0 else co.z), co.y
            else:                            # 앞/뒤 면 -> XY 투영
                u, v = (co.x if n.z >= 0 else -co.x), co.y
            uv.data[li].uv = (u * inv, v * inv)


def cylinder_uv(obj, tile=1.0, wraps=1.0, axis="Y", layer_name="UVMap"):
    """원통(줄기) 투영 UV. **둘레 -> U, 축 방향 높이 -> V.**

    box_uv 는 세로로 긴 원통에서 앞/뒤/좌/우 네 면으로 갈라져 이음새가 90도마다 생긴다.
    줄기(야자수 껍질·대나무 마디)는 세로결 텍스처(bark/bamboo)를 세로로 흘려야 하므로
    각도를 U 로 쓰는 이 함수를 쓴다.

    wraps: 한 바퀴에 텍스처가 몇 번 반복되는가(둘레 2m 에 wraps=3 이면 66cm 마다 반복).
    tile : 축 방향으로 텍스처 한 장이 덮는 거리(미터).

    **이음새 처리**: 각도를 그대로 쓰면 U 가 0.98 -> 0.02 로 되감기는 면이 한 줄 생기고
    그 면만 텍스처가 통째로 거꾸로 흐른다(rock 의 box_uv 이음새와 달리 세로결에서는 바로 보인다).
    면 단위로 U 폭이 반 바퀴를 넘으면 작은 쪽에 +1 바퀴를 더해 되감기를 없앤다.
    UV 언랩 오퍼레이터를 쓰지 않으므로 headless 에서 완전히 결정적이다.
    """
    mesh = obj.data
    uv = mesh.uv_layers.get(layer_name) or mesh.uv_layers.new(name=layer_name)
    ai = {"X": 0, "Y": 1, "Z": 2}[axis.upper()]
    # 둘레를 재는 두 축(축 순서를 고정해 회전 방향이 항상 같게 한다).
    a0, a1 = {0: (1, 2), 1: (2, 0), 2: (0, 1)}[ai]
    inv_v = 1.0 / max(tile, EPS)

    for poly in mesh.polygons:
        turns = []
        heights = []
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            turns.append((math.atan2(co[a1], co[a0]) / math.tau) % 1.0)
            heights.append(co[ai])
        if max(turns) - min(turns) > 0.5:          # 이음새를 지나는 면
            turns = [t + 1.0 if t < 0.5 else t for t in turns]
        for li, turn, h in zip(poly.loop_indices, turns, heights):
            uv.data[li].uv = (turn * wraps, h * inv_v)


def planar_uv(obj, axis="Y", tile=1.0, offset=(0.0, 0.0), layer_name="UVMap"):
    """평면(판형) 투영 UV. 잎처럼 **두께가 없는 단면 메시** 전용.

    axis 를 판의 법선으로 보고 나머지 두 축을 그대로 UV 로 쓴다. box_uv 와 달리 면 법선을
    보지 않으므로 **앞면과 뒷면이 정확히 같은 UV** 를 받는다(양면 잎에서 앞뒤 무늬가
    어긋나지 않는다). 잎을 로컬 좌표(길이 +Z, 폭 +X)로 만든 **직후**, 제자리로 옮기기
    전에 부르는 것을 전제로 한다 - 옮긴 뒤에 부르면 방향이 제각각인 잎들이 전부
    같은 월드 축으로 투영돼 무늬가 눕는다.

    tile 은 스칼라 또는 (U 방향 미터, V 방향 미터) 쌍이다. 잎은 폭보다 길이가 훨씬 길어서
    한 값으로는 무늬가 늘어난다 - 잎 하나가 텍스처 한 장을 정확히 덮게 두 축을 따로 준다.
    """
    mesh = obj.data
    uv = mesh.uv_layers.get(layer_name) or mesh.uv_layers.new(name=layer_name)
    ai = {"X": 0, "Y": 1, "Z": 2}[axis.upper()]
    a0, a1 = {0: (2, 1), 1: (0, 2), 2: (0, 1)}[ai]
    tu, tv = tile if isinstance(tile, (tuple, list)) else (tile, tile)
    inv_u, inv_v = 1.0 / max(tu, EPS), 1.0 / max(tv, EPS)
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            uv.data[li].uv = (co[a0] * inv_u + offset[0], co[a1] * inv_v + offset[1])


def swept_tube(bm, rings, sides=8, cap_bottom=True, cap_top=True, smooth=True):
    """줄기용 다각 튜브. rings = [(중심 Vector, 반지름)] 또는 [(중심, [측면별 반지름])].

    단면은 **수평(XZ) 원**이다. 진짜 접선 수직 단면이 아니라서 기울기 25도에서 굵기가 10%
    두꺼워 보이지만, 그 대신 (a) 마디/잎자국 링이 항상 수평으로 남고(야자수·대나무의
    실제 모습이 그렇다) (b) 링마다 프레임을 다시 잡지 않아 비틀림 아티팩트가 없다.

    반지름을 측면별 리스트로 주면 단면이 완전한 원이 아니게 되어 **프리미티브 원기둥**과
    실루엣이 갈린다 - 이 파이프라인에서 그게 통과/실패를 가르는 기준이다.

    감김은 신경 쓰지 않는다. 캡을 붙여 닫힌 다면체로 만든 뒤 recalc_face_normals 로
    한 번에 바깥을 향하게 한다(감김 표를 손으로 맞추다 뒤집힌 사고가 이 저장소에 여러 번 있다).

    smooth 는 bool 또는 **띠(band)별 리스트**(길이 = 링 수 - 1)다. 띠마다 다르게 주면
    한 튜브 안에서 매끈한 구간과 각진 구간을 섞을 수 있다 - 대나무 마디 칼라를 플랫으로
    두면 법선이 끊겨 **밝기 링**이 생기고, 그게 멀리서 마디를 읽게 하는 유일한 수단이다
    (기하 굴곡만으로는 20m 밖에서 한 픽셀도 안 남는다 - 실측).
    """
    if len(rings) < 2:
        raise ContractError("swept_tube: 링이 2개 미만이다")
    loops = []
    for center, radius in rings:
        radii = radius if isinstance(radius, (list, tuple)) else [radius] * sides
        loop = []
        for i in range(sides):
            a = math.tau * i / sides
            loop.append(bm.verts.new((center[0] + math.cos(a) * radii[i],
                                      center[1],
                                      center[2] + math.sin(a) * radii[i])))
        loops.append(loop)

    bands = smooth if isinstance(smooth, (list, tuple)) else [smooth] * (len(rings) - 1)
    if len(bands) != len(rings) - 1:
        raise ContractError(
            f"swept_tube: smooth 리스트 길이 {len(bands)} != 띠 수 {len(rings) - 1}")

    faces = []
    for b, (lo, hi) in enumerate(zip(loops, loops[1:])):
        for i in range(sides):
            j = (i + 1) % sides
            f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
            f.smooth = bands[b]
            faces.append(f)
    caps = []
    if cap_bottom:
        caps.append(bm.faces.new(loops[0]))
    if cap_top:
        caps.append(bm.faces.new(loops[-1]))
    for f in caps:
        f.smooth = False           # 캡은 언제나 평면이다 - 스무스로 두면 옆면과 법선이 섞인다
    bmesh.ops.recalc_face_normals(bm, faces=faces + caps)
    return loops


def make_double_sided(bm):
    """bmesh 의 모든 면을 **두께 없이** 양면으로 만든다(뒤집힌 복제면을 겹쳐 놓는다).

    잎은 단면 메시라야 한다(두께를 주면 삼각형이 배로 들고 얇은 판이 각목이 된다).
    그런데 URP Lit 은 기본이 백페이스 컬링이라 단면 잎은 뒤에서 보면 **사라진다**.
    이 프로젝트에는 알파 컷아웃/양면 셰이더 설정이 없으므로(계약 4장: 머티리얼은 런타임 코드가
    만든다 - 그 코드는 이번 배치의 락 밖이다) 해법은 메시에 뒷면을 굽는 것뿐이다.

    복제 정점은 **원본과 정확히 같은 위치**에 새로 만든다. 같은 정점을 공유해 감김만 뒤집는
    면은 bmesh 가 "이미 있는 면"으로 거절하고, 공유하면 스무스 셰이딩에서 앞뒤 법선이 평균돼
    0 이 된다. 그래서 이 함수를 거친 메시에는 **clean_bmesh(remove_doubles)를 부르면 안 된다** -
    복제 정점이 다시 녹아 뒷면이 통째로 사라진다.
    """
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.duplicate(bm, geom=geom)
    dup_faces = [g for g in res["geom"] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.reverse_faces(bm, faces=dup_faces)
    return bm


def join_objects(objs, name=None, remove_sources=True):
    """오브젝트 여러 개를 **메시 한 장**으로 합친다(드로우콜 절약).

    각 오브젝트의 변환을 정점에 구워서 합치므로 자식 회전이 남지 않는다(계약 1장).
    UV 레이어는 이름이 같으면 그대로 이어 붙는다 - 그래서 합치기 **전에** 각 조각의 UV 를
    펴 두는 것이 정상 경로다(줄기는 cylinder_uv, 잎은 planar_uv).
    폴리곤의 smooth 플래그도 보존된다(줄기는 스무스, 잎은 플랫을 섞을 수 있다).

    bpy.ops.object.join 을 쓰지 않는다 - 그쪽은 활성 오브젝트/컨텍스트에 의존해서
    headless 에서 조용히 실패한다. bmesh 누적은 컨텍스트가 없다.
    """
    objs = list(objs)
    if not objs:
        raise ContractError("join_objects: 합칠 오브젝트가 없다")
    bm = bmesh.new()
    for o in objs:
        tmp = o.data.copy()
        tmp.transform(o.matrix_world)
        bm.from_mesh(tmp)          # from_mesh 는 지우지 않고 **덧붙인다**(확인함)
        bpy.data.meshes.remove(tmp, do_unlink=True)
    joined = new_object(name or objs[0].name, bm)
    if remove_sources:
        for o in objs:
            data = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if data.users == 0:
                bpy.data.meshes.remove(data, do_unlink=True)
    return joined


def apply_transform(obj):
    """오브젝트 변환을 정점에 굽는다(계약 1장: 런타임 회전 금지)."""
    mesh = obj.data
    mesh.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)


def bbox(obj):
    """월드가 아니라 **메시 로컬** 바운딩 박스(apply_transform 후에는 같다)."""
    co = [v.co for v in obj.data.vertices]
    if not co:
        raise ContractError(f"{obj.name}: 정점이 0개다")
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    return lo, hi


def fit_size(obj, size):
    """축마다 따로 정규화해 바운딩 박스를 정확히 size(미터)로 만든다.

    균일 배율로 하면 정점 흔들림 때문에 실제 크기가 지정값과 어긋난다
    (게임 쪽 WorldMeshBuilder.AddChunk 가 같은 이유로 같은 처리를 한다).
    """
    lo, hi = bbox(obj)
    ext = hi - lo
    scale = Vector((size[0] / max(ext.x, EPS),
                    size[1] / max(ext.y, EPS),
                    size[2] / max(ext.z, EPS)))
    obj.data.transform(Matrix.Diagonal(scale.to_4d()))


# ── 계약 강제 ─────────────────────────────────────────────────────────────────
def ground_center(objs, band=None):
    """**접지 중심**: 지면에 닿는 부분(밑동·클럼프 밑면)의 XZ 중심.

    계약 1장의 "X/Z 중심 정렬"을 오래 **바운딩 박스 중심**으로 읽어 왔는데, 비대칭 물체에서는
    그게 틀렸다. 휜 야자수는 크라운이 bbox 를 지배해서 줄기 밑동이 원점에서 최대 0.74m
    밀려났고(실측), 그러면 스폰 지점에 나무가 안 선다. 지면에 닿는 것은 밑동이므로
    **밑동의 중심이 원점**이어야 맞다. 바운딩 박스는 비대칭이어도 된다 - 그게 자연스럽다.

    band: 밑면에서 이 높이 안에 있는 정점만 "접지부"로 본다(기본 = 높이의 1.5%, 최소 3cm).
    좁게 잡으면 밑면 링 한 줄만 잡혀 정확하고, 넓게 잡으면 밑동 플레어까지 섞인다.
    """
    objs = _as_objects(objs)
    lo, hi = union_bbox(objs)
    if band is None:
        band = max(0.03, (hi.y - lo.y) * 0.015)
    limit = lo.y + band
    sx = sz = 0.0
    n = 0
    for o in objs:
        for v in o.data.vertices:
            if v.co.y <= limit:
                sx += v.co.x
                sz += v.co.z
                n += 1
    if n == 0:                                  # 있을 수 없지만 방어(밑면이 비면 bbox 중심으로)
        return Vector(((lo.x + hi.x) * 0.5, lo.y, (lo.z + hi.z) * 0.5))
    return Vector((sx / n, lo.y, sz / n))


def _align_offset(objs, align, band=None):
    """정렬 오프셋 하나를 계산한다(밑면을 y=0 으로, XZ 를 align 규약의 중심으로)."""
    objs = _as_objects(objs)
    lo, hi = union_bbox(objs)
    if align == "ground":
        c = ground_center(objs, band)
        return Vector((-c.x, -lo.y, -c.z))
    if align == "bbox":
        return Vector((-(lo.x + hi.x) * 0.5, -lo.y, -(lo.z + hi.z) * 0.5))
    raise ContractError(f"align 은 'bbox' 또는 'ground' 다 (받은 값: {align!r})")


def enforce_contract(obj, tri_budget, expect_size=None, size_tol=0.005,
                     tri_floor=None, name=None, align="bbox", ground_band=None,
                     sink=0.0):
    """계약을 **적용하고 검증한다**. 하나라도 어기면 ContractError.

    적용:
      (a) 밑면을 y = 0 으로 내린다
      (b) X / Z 를 align 규약의 중심으로 정렬한다
      (c) 오브젝트 변환을 정점에 굽는다
    검증:
      (1) 모든 면이 삼각형인가
      (2) 삼각형 수 <= tri_budget (그리고 tri_floor 를 주면 그 이상인가)
      (3) 밑면 y == 0, X/Z 중심 == 0 (허용 오차 1e-5)
      (4) y = 0 아래로 새어 나간 정점이 없는가
      (5) 크기가 0 인 축이 없는가 / expect_size 를 주면 그 값과 일치하는가
          -> **축 뒤바뀜(Z-up 반입 사고)이 여기서 잡힌다**
      (6) 퇴화 삼각형(면적 0)이 없는가 - 법선이 0 이 되어 그 면만 새까맣게 보인다
      (7) 남은 오브젝트 변환이 항등인가

    align:
      "bbox"   - 바운딩 박스 중심(기본, 대칭 물체용). 바위가 이걸 쓴다.
      "ground" - **접지 중심**(밑동의 XZ 중심). 야자수·대나무처럼 위가 비대칭인 물체용.
                 bbox 로 맞추면 크라운이 bbox 를 지배해 밑동이 원점에서 밀려나고,
                 그러면 스폰 지점에 나무가 안 선다(야자수 실측 최대 0.74m). ground_center 주석 참고.
      기본값이 "bbox" 인 이유는 하나뿐이다 - 이미 배포된 rock_a/b/c 의 바이트가 바뀌면 안 된다.
      새 에셋은 대칭이 확실한 경우가 아니면 "ground" 를 쓴다.

    sink (2026-08-17 절벽 배치용 확장 - 기본 0.0 이면 기존 동작과 바이트 단위로 같다):
      메시 밑면을 y = -sink 까지 **의도적으로** 내려 보낸다. 원점(y=0)은 여전히 **접지 기준**이고,
      밑면만 지면 아래 여유분이다 - 경사면에 얹어도 앞모서리가 뜨지 않게 하는 소품(절벽)용.
      검사도 같이 옮겨 간다: 밑면 == -sink, -sink 아래로 새어 나간 정점 금지.
      expect_size 의 H 는 **bbox 전체 높이**(지상 높이 + sink)다.

    반환: 보고·검증에 쓰는 dict(name/tris/verts/size/…).
    """
    label = name or obj.name

    apply_transform(obj)

    mesh = obj.data
    for poly in mesh.polygons:
        if len(poly.vertices) != 3:
            raise ContractError(
                f"{label}: 삼각형이 아닌 면이 있다(정점 {len(poly.vertices)}개). "
                f"triangulate() 를 먼저 불러라 - 계약 3장.")

    tris = len(mesh.polygons)
    if tris > tri_budget:
        raise ContractError(f"{label}: 삼각형 {tris} > 예산 {tri_budget} (계약 2장)")
    if tri_floor is not None and tris < tri_floor:
        raise ContractError(f"{label}: 삼각형 {tris} < 하한 {tri_floor} — 너무 성기다")

    # (a)(b) 원점 정렬. sink 만큼 통째로 내린다(0 이면 기존 경로 그대로).
    off = _align_offset(obj, align, ground_band)
    off.y -= sink
    mesh.transform(Matrix.Translation(off))

    lo, hi = bbox(obj)
    size = hi - lo
    if abs(lo.y + sink) > EPS:
        raise ContractError(f"{label}: 밑면 y={lo.y:.6f} != {-sink:.3f} (계약 1장/sink)")
    if align == "ground":
        c = ground_center(obj, ground_band)
        if abs(c.x) > EPS or abs(c.z) > EPS:
            raise ContractError(
                f"{label}: 접지 중심이 어긋났다 (x {c.x:.6f}, z {c.z:.6f})")
    elif abs(lo.x + hi.x) > EPS or abs(lo.z + hi.z) > EPS:
        raise ContractError(
            f"{label}: X/Z 중심이 어긋났다 (x중심 {(lo.x + hi.x) * 0.5:.6f}, "
            f"z중심 {(lo.z + hi.z) * 0.5:.6f})")
    below = min((v.co.y for v in mesh.vertices), default=0.0)
    if below < -sink - EPS:
        raise ContractError(
            f"{label}: 허용 밑면(y={-sink:.3f}) 아래로 새어 나간 정점이 있다(min y={below:.6f})")
    if min(size.x, size.y, size.z) <= EPS:
        raise ContractError(f"{label}: 두께가 0 인 축이 있다 {tuple(round(v, 4) for v in size)}")

    if expect_size is not None:
        for axis, want, got in zip("XYZ", expect_size, (size.x, size.y, size.z)):
            if abs(want - got) > size_tol:
                raise ContractError(
                    f"{label}: {axis} 크기 {got:.4f}m 가 기대값 {want:.4f}m 와 다르다. "
                    f"축이 뒤바뀌었을 수 있다(Z-up 반입 사고 - 계약 0장).")

    degenerate = sum(1 for p in mesh.polygons if p.area < 1e-9)
    if degenerate:
        raise ContractError(f"{label}: 면적 0 인 삼각형 {degenerate}개 — 법선이 0 이 되어 새까맣게 보인다")

    if obj.matrix_world != Matrix.Identity(4):
        raise ContractError(f"{label}: 오브젝트 변환이 남아 있다 — 정점에 구워라(계약 1장)")

    has_uv = len(mesh.uv_layers) > 0
    return {
        "name": label,
        "tris": tris,
        "verts": len(mesh.vertices),
        "size": (size.x, size.y, size.z),
        "uv": has_uv,
        "budget": tri_budget,
        "align": align,
        "ground_band": ground_band,
        "sink": sink,
    }


def _as_objects(obj_or_objs):
    """단일 오브젝트도 리스트도 받는다(기존 호출부 시그니처를 깨지 않기 위한 어댑터)."""
    if isinstance(obj_or_objs, (list, tuple)):
        return list(obj_or_objs)
    return [obj_or_objs]


def union_bbox(objs):
    """여러 오브젝트를 하나로 본 바운딩 박스. 조립체(줄기 + 왕관)의 접지 검사에 쓴다."""
    los, his = [], []
    for o in objs:
        lo, hi = bbox(o)
        los.append(lo); his.append(hi)
    lo = Vector((min(v.x for v in los), min(v.y for v in los), min(v.z for v in los)))
    hi = Vector((max(v.x for v in his), max(v.y for v in his), max(v.z for v in his)))
    return lo, hi


def enforce_contract_group(objs, tri_budget, expect_size=None, size_tol=0.005,
                           tri_floor=None, name=None, align="ground", ground_band=None):
    """**조립체용** enforce_contract. 여러 오브젝트를 하나의 에셋으로 보고 계약을 강제한다.

    왜 필요한가: 야자수는 줄기(bark/갈색)와 왕관(frond/초록)의 색이 다르다. 계약 4장이
    "머티리얼은 런타임 코드가 만든다"이므로 색이 둘이면 렌더러도 둘이어야 하는데,
    한 오브젝트로 합쳐 버리면 나무 전체가 단색이 된다. 그렇다고 파일을 둘로 쪼개면
    왕관 OBJ 는 제 밑면이 y=0 이 되어 **줄기 꼭대기에 얹히는 오프셋을 잃는다**(계약 1장의
    접지 규칙이 조립 관계를 지운다). 그래서 파일은 하나, 그 안에 `o` 오브젝트 둘로 둔다 -
    Unity OBJ 임포터가 `o` 를 자식 GameObject 로 만들고 **상대 위치를 그대로 보존한다**
    (IslandMeshGenerator.cs:2094 의 기존 주석이 이 동작을 이미 전제하고 있다).

    enforce_contract 와 다른 점은 둘이다.
     (1) 접지·중심 정렬·크기 검사를 **합집합**에 대해 하고, 정렬 오프셋을 모든 오브젝트에
         **똑같이** 먹여 상대 위치를 1mm도 흔들지 않는다.
     (2) align 기본값이 **"ground"** 다. 조립체는 정의상 비대칭이라(왕관이 줄기 위에 있다)
         바운딩 박스 중심을 쓰면 밑동이 원점에서 밀려난다 - ground_center 주석 참고.
    나머지 검사(삼각형만·예산·퇴화면·잔여 변환)는 오브젝트마다 그대로 돈다.
    """
    objs = _as_objects(objs)
    label = name or objs[0].name
    if not objs:
        raise ContractError(f"{label}: 오브젝트가 0개다")

    for o in objs:
        apply_transform(o)
        for poly in o.data.polygons:
            if len(poly.vertices) != 3:
                raise ContractError(
                    f"{label}/{o.name}: 삼각형이 아닌 면이 있다(정점 {len(poly.vertices)}개) - 계약 3장.")

    tris = sum(len(o.data.polygons) for o in objs)
    verts = sum(len(o.data.vertices) for o in objs)
    if tris > tri_budget:
        raise ContractError(f"{label}: 삼각형 {tris} > 예산 {tri_budget} (계약 2장)")
    if tri_floor is not None and tris < tri_floor:
        raise ContractError(f"{label}: 삼각형 {tris} < 하한 {tri_floor} — 너무 성기다")

    offset = Matrix.Translation(_align_offset(objs, align, ground_band))
    for o in objs:
        o.data.transform(offset)

    lo, hi = union_bbox(objs)
    size = hi - lo
    if abs(lo.y) > EPS:
        raise ContractError(f"{label}: 밑면 y={lo.y:.6f} != 0 (계약 1장)")
    if align == "ground":
        c = ground_center(objs, ground_band)
        if abs(c.x) > EPS or abs(c.z) > EPS:
            raise ContractError(f"{label}: 접지 중심이 어긋났다 (x {c.x:.6f}, z {c.z:.6f})")
    elif abs(lo.x + hi.x) > EPS or abs(lo.z + hi.z) > EPS:
        raise ContractError(f"{label}: X/Z 중심이 어긋났다")
    if min(size.x, size.y, size.z) <= EPS:
        raise ContractError(f"{label}: 두께가 0 인 축이 있다 {tuple(round(v, 4) for v in size)}")

    if expect_size is not None:
        for axis, want, got in zip("XYZ", expect_size, (size.x, size.y, size.z)):
            if abs(want - got) > size_tol:
                raise ContractError(
                    f"{label}: {axis} 크기 {got:.4f}m 가 기대값 {want:.4f}m 와 다르다. "
                    f"축이 뒤바뀌었을 수 있다(Z-up 반입 사고 - 계약 0장).")

    for o in objs:
        degenerate = sum(1 for p in o.data.polygons if p.area < 1e-9)
        if degenerate:
            raise ContractError(
                f"{label}/{o.name}: 면적 0 인 삼각형 {degenerate}개 — 법선이 0 이 되어 새까맣게 보인다")
        if o.matrix_world != Matrix.Identity(4):
            raise ContractError(f"{label}/{o.name}: 오브젝트 변환이 남아 있다 — 정점에 구워라(계약 1장)")

    return {
        "name": label,
        "tris": tris,
        "verts": verts,
        "size": (size.x, size.y, size.z),
        "uv": all(len(o.data.uv_layers) > 0 for o in objs),
        "budget": tri_budget,
        "align": align,
        "ground_band": ground_band,
        "parts": [(o.name, len(o.data.polygons)) for o in objs],
    }


# ── 내보내기 ──────────────────────────────────────────────────────────────────
def export_obj(obj, path):
    """OBJ 로 내보낸다. 삼각형화·정점 법선 포함·머티리얼 미포함(계약 3장).

    **forward_axis='Y' / up_axis='Z' 가 "축 변환 없음"이다.** 직관과 반대라서 한 번 걸렸다:
    이 두 값은 "Blender 의 어느 축을 출력의 forward/up 으로 쓸 것인가"를 뜻하고,
    Blender 기본 축(forward=+Y, up=+Z)을 그대로 지정하면 항등 변환이 된다.
    기본값(forward=-Z, up=Y)은 Blender Z-up -> OBJ Y-up 변환이라, 이미 Y-up 으로 만든
    메시를 넣으면 **Y 와 Z 가 맞바뀐다**(실측: H 1.20m 가 파일에서 1.60m 로 나왔다).
    이 파일은 처음부터 Unity 좌표(X 오른쪽 / Y 위 / Z 정면)로 메시를 만들고 그대로 적는다.
    verify_obj_file 이 이 사고를 잡는다 - 이 검사를 빼지 마라.

    오브젝트 **리스트**를 주면 한 파일 안에 `o` 그룹 여러 개로 나간다(조립체 - 줄기 + 왕관).
    Unity OBJ 임포터가 `o` 를 자식 GameObject 로 만들어 상대 위치를 보존하므로,
    파츠마다 다른 런타임 머티리얼(갈색 줄기 / 초록 잎)을 물릴 수 있다. 단일 오브젝트를 줄 때의
    동작은 한 글자도 바뀌지 않는다(rock.py 가 바이트 단위로 같은 파일을 낸다 - 확인함).
    """
    objs = _as_objects(obj)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.wm.obj_export(
        filepath=path,
        export_selected_objects=True,
        apply_modifiers=True,
        export_triangulated_mesh=True,
        export_normals=True,
        export_uv=True,
        export_materials=False,
        export_colors=False,
        forward_axis="Y",
        up_axis="Z",
        global_scale=1.0,
        path_mode="AUTO",
    )
    return path


def verify_obj_file(path, stats, size_tol=0.005):
    """**내보낸 파일을 다시 읽어** 계약을 재확인한다.

    Blender 안에서 통과한 것과 디스크에 적힌 것이 다를 수 있다(축 변환·스케일 옵션).
    곰에서 실제로 난 사고가 정확히 이 틈이라 여기서 한 번 더 막는다.
    """
    xs, ys, zs = [], [], []
    faces = 0
    normals = 0
    uvs = 0
    with open(path, "r") as fh:
        for line in fh:
            if line.startswith("v "):
                _, x, y, z = line.split()[:4]
                xs.append(float(x)); ys.append(float(y)); zs.append(float(z))
            elif line.startswith("vn "):
                normals += 1
            elif line.startswith("vt "):
                uvs += 1
            elif line.startswith("f "):
                n = len(line.split()) - 1
                if n != 3:
                    raise ContractError(f"{path}: 삼각형이 아닌 면(f {n}개 정점)이 파일에 있다")
                faces += 1
            elif line.startswith(("mtllib", "usemtl")):
                raise ContractError(f"{path}: 머티리얼 참조가 들어갔다(계약 3장: .mtl 없이 내보낸다)")

    if faces != stats["tris"]:
        raise ContractError(f"{path}: 파일 삼각형 {faces} != 메시 {stats['tris']}")
    if normals == 0:
        raise ContractError(f"{path}: vn(정점 법선)이 없다 — 계약 3장")
    if stats["uv"] and uvs == 0:
        raise ContractError(f"{path}: UV 를 폈는데 vt 가 파일에 없다")

    size = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    for axis, want, got in zip("XYZ", stats["size"], size):
        if abs(want - got) > size_tol:
            raise ContractError(
                f"{path}: 파일의 {axis} 크기 {got:.4f}m 가 메시 {want:.4f}m 와 다르다 "
                f"(내보내기 축 변환/배율 사고)")
    sink = stats.get("sink", 0.0) or 0.0
    if abs(min(ys) + sink) > 1e-3:
        raise ContractError(f"{path}: 파일 밑면 y={min(ys):.5f} != {-sink:.3f}")

    # 원점 규약을 **파일에서 다시 계산해** 대조한다. ground 모드는 bbox 중심이 0 이 아닌 것이
    # 정상이므로(비대칭 크라운) 접지부 중심을 같은 방식으로 다시 구해 검사한다.
    if stats.get("align", "bbox") == "ground":
        band = stats.get("ground_band")
        if band is None:
            band = max(0.03, (max(ys) - min(ys)) * 0.015)
        limit = min(ys) + band
        gx = [x for x, y in zip(xs, ys) if y <= limit]
        gz = [z for z, y in zip(zs, ys) if y <= limit]
        cx, cz = sum(gx) / len(gx), sum(gz) / len(gz)
        if abs(cx) > 1e-3 or abs(cz) > 1e-3:
            raise ContractError(
                f"{path}: 파일의 접지 중심이 어긋났다 (x {cx:.5f}, z {cz:.5f})")
    elif abs(min(xs) + max(xs)) > 1e-3 or abs(min(zs) + max(zs)) > 1e-3:
        raise ContractError(f"{path}: 파일의 X/Z 중심이 어긋났다")

    stats = dict(stats)
    stats["bytes"] = os.path.getsize(path)
    stats["path"] = path
    return stats


def inject_usemtl(obj_path):
    """각 `o <이름>` 줄 뒤에 `usemtl <이름>`을 넣고, 같은 이름의 .mtl 파일을 함께 쓴다.

    [실사고 2건의 종착지] Unity 6.5 OBJ 임포터는 서브메시를 머티리얼 단위로 만드는데,
    (1) usemtl이 아예 없으면 서브메시 1개로 병합되고(0.2.13 "색상이 회색"),
    (2) usemtl이 있어도 mtllib가 가리키는 실제 .mtl에서 해석되지 않으면 무시되어
        여전히 서브메시 1개다(0.2.21 검증에서 "병합 메시 sub1" 로그로 발각).
    그래서 mtllib 선언 + newmtl 목록이 든 최소 .mtl을 동봉한다. 계약 3장("외부 .mtl 의존
    금지")의 취지는 "머티리얼은 런타임 코드가 만든다"이고, 이 .mtl은 색을 정의하는 파일이
    아니라 서브메시 구분자다(런타임이 어차피 MG~ 머티리얼로 갈아끼운다).

    **호출 순서 계약**: 반드시 export_obj → verify_obj_file 을 통과한 파일에 마지막으로
    부른다. verify_obj_file 은 mtllib/usemtl 을 발견하면 ContractError 로 거절하므로
    (계약 3장), 이 함수를 먼저 부르면 검증이 통째로 죽는다 - 지오메트리 검증을 통과한
    파일에 서브메시 구분자만 덧붙이는 것이 정상 경로다. export_obj 안에 접지 마라.
    """
    with open(obj_path, "r") as fh:
        lines = fh.readlines()
    base = os.path.basename(obj_path)
    mtl_name = base[:-4] + ".mtl"
    names = []
    out = []
    header_done = False
    for line in lines:
        out.append(line)
        if not header_done and not line.startswith("#"):
            out.insert(len(out) - 1, "mtllib " + mtl_name + chr(10))
            header_done = True
        if line.startswith("o "):
            name = line[2:].strip()
            names.append(name)
            out.append("usemtl " + name + chr(10))
    with open(obj_path, "w") as fh:
        fh.writelines(out)
    mtl_path = os.path.join(os.path.dirname(obj_path), mtl_name)
    with open(mtl_path, "w") as fh:
        for n in names:
            fh.write("newmtl " + n + chr(10) + "Kd 0.8 0.8 0.8" + chr(10) + chr(10))


# ── 미리보기용 머티리얼 (Unity 로는 안 나간다) ────────────────────────────────
def preview_material(name, texture_name=None, base_color=(0.5, 0.5, 0.52), roughness=0.85,
                     uv_scale=1.0):
    """렌더 검수용 머티리얼. 계약 4장대로 **Unity 머티리얼 에셋은 만들지 않는다** —
    이건 Blender 안에서만 사는 물건이고 OBJ 에도 안 실린다.

    texture_name 을 주면 Resources/Textures/<name>.png 를 albedo 로 곱해 UV 이음새를
    렌더에서 눈으로 확인할 수 있게 한다.
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Base Color"].default_value = (*base_color, 1.0)

    if texture_name:
        tex_path = os.path.join(TEXTURES_DIR, f"{texture_name}.png")
        if os.path.exists(tex_path):
            img = bpy.data.images.load(tex_path, check_existing=True)
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.image = img
            mapping = nt.nodes.new("ShaderNodeMapping")
            mapping.inputs["Scale"].default_value = (uv_scale, uv_scale, uv_scale)
            uvnode = nt.nodes.new("ShaderNodeUVMap")
            mix = nt.nodes.new("ShaderNodeMixRGB")
            mix.blend_type = "MULTIPLY"
            mix.inputs["Fac"].default_value = 1.0
            mix.inputs["Color2"].default_value = (*base_color, 1.0)
            nt.links.new(uvnode.outputs["UV"], mapping.inputs["Vector"])
            nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
            nt.links.new(tex.outputs["Color"], mix.inputs["Color1"])
            nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def assign_material(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


# ── 턴테이블 렌더 ─────────────────────────────────────────────────────────────
_VIEWS = [
    ("FRONT  +Z", (0.0, 0.0, 1.0), 0.0),
    ("SIDE   +X", (1.0, 0.0, 0.0), 0.0),
    ("BACK   -Z", (0.0, 0.0, -1.0), 0.0),
    ("3/4", (0.78, 0.0, 0.78), 26.0),
]


def _build_ground(extent):
    """y=0 기준 격자 바닥. **1칸 = 1m** 라 렌더만 보고 크기를 읽을 수 있다."""
    half = extent
    bm = bmesh.new()
    v = [bm.verts.new(p) for p in ((-half, 0, -half), (half, 0, -half),
                                   (half, 0, half), (-half, 0, half))]
    bm.faces.new(v)
    obj = new_object("PreviewGround", bm)

    mat = bpy.data.materials.new("PreviewGroundMat")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 1.0
    checker = nt.nodes.new("ShaderNodeTexChecker")
    checker.inputs["Color1"].default_value = (0.30, 0.32, 0.34, 1)
    checker.inputs["Color2"].default_value = (0.21, 0.23, 0.25, 1)
    checker.inputs["Scale"].default_value = 1.0  # 오브젝트 좌표 = 미터 -> 1m 격자
    coord = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(coord.outputs["Object"], checker.inputs["Vector"])
    nt.links.new(checker.outputs["Color"], bsdf.inputs["Base Color"])
    obj.data.materials.append(mat)
    return obj


def _setup_world_and_light():
    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.30, 0.34, 0.40, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.75
    bpy.context.scene.world = world

    sun_data = bpy.data.lights.new("PreviewSun", type="SUN")
    sun_data.energy = 4.3
    sun_data.angle = math.radians(6.0)
    sun = bpy.data.objects.new("PreviewSun", sun_data)
    bpy.context.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(52), math.radians(18), math.radians(35))

    fill_data = bpy.data.lights.new("PreviewFill", type="SUN")
    fill_data.energy = 1.7
    fill = bpy.data.objects.new("PreviewFill", fill_data)
    bpy.context.collection.objects.link(fill)
    fill.rotation_euler = (math.radians(64), 0.0, math.radians(-135))


def turntable(obj, out_png, title="", stats=None, px=440, samples=24, notes=None):
    """정면·측면·후면·3/4 네 컷을 **PNG 한 장**으로 합친다(디렉터가 한 번에 본다).

    - 전부 **정사영(ortho)** 이고 네 컷의 배율이 같다 -> 컷끼리 크기를 직접 비교할 수 있다.
    - 바닥은 1m 격자. 정면/측면/후면은 카메라가 수평이라 **바닥선이 곧 y=0** 이다
      -> 접지(발/밑면이 y=0)를 눈으로 바로 검수할 수 있다(계약 6장).
    - 크기(가로·세로·길이 m)·삼각형 수·UV 유무를 이미지에 굽는다.

    obj 에 **리스트**를 줘도 된다(조립체). 그때는 합집합 바운딩 박스로 카메라를 잡는다.
    """
    from PIL import Image, ImageDraw, ImageFont

    lo, hi = union_bbox(_as_objects(obj))
    size = hi - lo
    span = max(size.x, size.y, size.z)
    center = Vector((0.0, size.y * 0.5, 0.0))

    ground = _build_ground(max(6.0, span * 3.0))
    _setup_world_and_light()

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.render.resolution_x = px
    scene.render.resolution_y = px
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = span * 1.55
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    os.makedirs(os.path.dirname(out_png), exist_ok=True)
    tmp_dir = os.path.join(os.path.dirname(out_png), "_tiles")
    os.makedirs(tmp_dir, exist_ok=True)

    tiles = []
    for i, (label, direction, elev) in enumerate(_VIEWS):
        d = Vector(direction).normalized()
        pitch = math.radians(elev)
        # 수평 방향 d 를 elev 만큼 들어 올린 시선 벡터.
        eye_dir = Vector((d.x * math.cos(pitch), math.sin(pitch), d.z * math.cos(pitch)))
        cam.location = center + eye_dir * (span * 6.0)
        # 카메라 기저를 **직접 만든다**. Vector.to_track_quat('-Z','Y') 를 쓰면 안 된다 -
        # 그 함수는 카메라 로컬 Y 를 **Blender 월드 Z**(Blender 의 위쪽)에 맞추는데,
        # 이 파이프라인은 Y-up(Unity 좌표)으로 메시를 만든다. 결과적으로 측면 컷이
        # 화면상 90도 돌아 나왔고(층이 세로로 섰다) 접지 검수가 통째로 무의미해졌다.
        # 실제로 첫 렌더에서 "SIDE 컷만 바위가 y=0 아래로 내려간 것처럼" 보인 원인이다.
        z_cam = eye_dir.normalized()                       # 카메라 뒤쪽(+Z)
        x_cam = Vector((0.0, 1.0, 0.0)).cross(z_cam)
        if x_cam.length < 1e-6:                            # 바로 위/아래에서 내려다볼 때
            x_cam = Vector((1.0, 0.0, 0.0))
        x_cam.normalize()
        y_cam = z_cam.cross(x_cam)
        cam.matrix_world = Matrix((
            (x_cam.x, y_cam.x, z_cam.x, cam.location.x),
            (x_cam.y, y_cam.y, z_cam.y, cam.location.y),
            (x_cam.z, y_cam.z, z_cam.z, cam.location.z),
            (0.0, 0.0, 0.0, 1.0),
        ))

        tile = os.path.join(tmp_dir, f"tile{i}.png")
        scene.render.filepath = tile
        bpy.ops.render.render(write_still=True)
        tiles.append((label, tile))

    # ── PIL 합성 ──
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    mono_path = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
    try:
        f_title = ImageFont.truetype(font_path, 22)
        f_label = ImageFont.truetype(font_path, 16)
        f_info = ImageFont.truetype(mono_path, 15)
    except OSError:  # 폰트가 없으면 렌더를 막지는 않는다
        f_title = f_label = f_info = ImageFont.load_default()

    header = 66
    sheet = Image.new("RGB", (px * 2, header + px * 2), (24, 26, 30))
    draw = ImageDraw.Draw(sheet)

    info = ""
    if stats:
        info = (f"tris {stats['tris']}/{stats['budget']}   "
                f"verts {stats['verts']}   "
                f"UV {'yes' if stats['uv'] else 'NONE'}")
    dims = f"W {size.x:.2f} m  x  H {size.y:.2f} m  x  D {size.z:.2f} m"
    draw.text((14, 8), title, font=f_title, fill=(235, 238, 242))
    draw.text((14, 36), dims, font=f_info, fill=(255, 208, 120))
    if info:
        draw.text((px * 2 - 14 - draw.textlength(info, font=f_info), 36), info,
                  font=f_info, fill=(150, 220, 170))
    if notes:
        draw.text((px * 2 - 14 - draw.textlength(notes, font=f_info), 10), notes,
                  font=f_info, fill=(150, 170, 200))

    # 정사영이라 월드 <-> 픽셀 대응이 정확하다. 이걸 이용해 **y=0 기준선**을 그린다.
    # 수평 카메라에서는 바닥판이 정확히 옆으로 서서 렌더에 한 픽셀도 안 나온다
    # (첫 렌더가 그래서 "바위가 공중에 뜬 것처럼" 보였다) - 선을 직접 그어야 접지를 검수할 수 있다.
    ppm = px / cam_data.ortho_scale               # pixels per meter
    for i, (label, tile) in enumerate(tiles):
        img = Image.open(tile).convert("RGB")
        x = (i % 2) * px
        y = header + (i // 2) * px
        sheet.paste(img, (x, y))
        draw.rectangle([x, y, x + px - 1, y + px - 1], outline=(70, 76, 84))

        if abs(_VIEWS[i][2]) < 1e-6:              # 수평 카메라 컷에만
            row = y + px * 0.5 + center.y * ppm
            draw.line([(x, row), (x + px, row)], fill=(0, 210, 255), width=1)
            draw.text((x + 8, row + 4), "y = 0", font=f_label, fill=(0, 210, 255))
            for m in range(-6, 7):                # 1m 눈금
                col = x + px * 0.5 + m * ppm
                if x < col < x + px:
                    draw.line([(col, row), (col, row + (9 if m else 15))],
                              fill=(0, 210, 255), width=1)
            # 플레이어 키(CharacterController height 2m) 기준 막대.
            bar_x = x + 24
            top = max(y + 4, row - 2.0 * ppm)      # 타일 밖으로 삐져나가지 않게 자른다
            draw.line([(bar_x, row), (bar_x, top)], fill=(255, 120, 90), width=2)
            draw.line([(bar_x - 6, top), (bar_x + 6, top)], fill=(255, 120, 90), width=2)
            # 라벨은 눈금 **아래**에 둔다. 위에 두면 낮은 에셋(높이 1.2m)에서 막대 꼭대기가
            # 타일 상단에 붙어 글자가 헤더로 잘려 나간다.
            draw.text((bar_x + 9, top + 3), "2 m player", font=f_label, fill=(255, 120, 90))

        draw.rectangle([x + 6, y + 6, x + 6 + int(draw.textlength(label, font=f_label)) + 12,
                        y + 30], fill=(18, 20, 24))
        draw.text((x + 12, y + 8), label, font=f_label, fill=(235, 238, 242))

    draw.text((px * 2 - 150, header + px * 2 - 22), "grid = 1 m", font=f_label,
              fill=(200, 205, 212))
    sheet.save(out_png)

    for _, tile in tiles:
        os.remove(tile)
    os.rmdir(tmp_dir)
    bpy.data.objects.remove(ground, do_unlink=True)
    return out_png


def report(stats):
    s = stats["size"]
    print(f"  {stats['name']:<10} tris {stats['tris']:>5}/{stats['budget']}  "
          f"verts {stats['verts']:>5}  "
          f"size {s[0]:.2f} x {s[1]:.2f} x {s[2]:.2f} m  "
          f"UV {'yes' if stats['uv'] else 'NONE'}"
          + (f"  -> {os.path.relpath(stats['path'], PROJECT_ROOT)}" if "path" in stats else ""))
    if stats.get("parts"):
        print("             파츠: " + " + ".join(f"{n} {t}" for n, t in stats["parts"]))
