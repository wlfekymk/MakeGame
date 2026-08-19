#!/usr/bin/env python3
"""생성된 배치를 SVG 한 장으로 그린다. 눈으로 분포를 확인하는 용도."""
import re, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
src = open(os.path.join(ROOT, "Assets/_Project/Scripts/Utils/MaldivesLayout.cs"), encoding="utf-8").read()
E = re.findall(r'sourceId\s*=\s*"([^"]+)",\s*size\s*=\s*IslandSize\.(\w+),\s*posX\s*=\s*(-?[\d.]+)f,\s*posZ\s*=\s*(-?[\d.]+)f', src)
R = {"Small":50,"Medium":90,"Large":140,"ExtraLarge":200}
C = {"Small":"#8AA84F","Medium":"#6BA83F","Large":"#C2B280","ExtraLarge":"#E6BF33"}
W = 900; PAD = 40
xs=[float(x) for _,_,x,_ in E]; zs=[float(z) for _,_,_,z in E]
lo = min(min(xs),min(zs)); hi = max(max(xs),max(zs)); span = hi-lo
def sx(x): return PAD + (x-lo)/span*(W-2*PAD)
def sy(z): return PAD + (hi-z)/span*(W-2*PAD)   # 북쪽이 위
o=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{W+60}" viewBox="0 0 {W} {W+60}">',
   f'<rect width="{W}" height="{W+60}" fill="#0d2438"/>',
   f'<rect x="{PAD}" y="{PAD}" width="{W-2*PAD}" height="{W-2*PAD}" fill="#12405f" stroke="#1a598c"/>']
# 시작 섬 기준 거리 링
s0x, s0z = float(E[0][2]), float(E[0][3])
for km in (5,10,15,20,25):
    r = km*1000/span*(W-2*PAD)
    o.append(f'<circle cx="{sx(s0x):.1f}" cy="{sy(s0z):.1f}" r="{r:.1f}" fill="none" stroke="#ffffff" stroke-opacity="0.10"/>')
    o.append(f'<text x="{sx(s0x)+r*0.7:.1f}" y="{sy(s0z)-r*0.7:.1f}" fill="#ffffff" fill-opacity="0.3" font-size="10" font-family="sans-serif">{km}km</text>')
for i,(sid,size,x,z) in enumerate(E):
    x,z=float(x),float(z)
    r = max(3.0, R[size]/span*(W-2*PAD)*3.0)   # 섬은 실제 크기보다 3배 크게 그려야 보인다
    o.append(f'<circle cx="{sx(x):.1f}" cy="{sy(z):.1f}" r="{r:.1f}" fill="{C[size]}" stroke="#0d2438" stroke-width="0.7"/>')
    if size in ("Large","ExtraLarge") or i==0:
        lbl = "시작" if i==0 else ("특대" if size=="ExtraLarge" else "대형")
        o.append(f'<text x="{sx(x)+r+3:.1f}" y="{sy(z)+3:.1f}" fill="#fff" font-size="11" font-family="sans-serif">{lbl}</text>')
d = math.hypot(0,0)
o.append(f'<text x="{PAD}" y="{W+22}" fill="#cfe2ee" font-size="13" font-family="sans-serif">span {span/1000:.1f} km · 섬 {len(E)}개 · 소형 36 / 중형 10 / 대형 3 / 특대 1</text>')
o.append(f'<text x="{PAD}" y="{W+42}" fill="#8aa8bc" font-size="11" font-family="sans-serif">원은 시작 섬 기준 거리. 섬 표시는 실제 크기의 3배로 그렸다.</text>')
o.append('</svg>')
open(os.path.join(HERE,"preview.svg"),"w",encoding="utf-8").write("\n".join(o))
print("preview.svg")
