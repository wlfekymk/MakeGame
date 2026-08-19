import asyncio, os, re
from playwright.async_api import async_playwright

# 경로는 이 스크립트 위치에서 스스로 찾는다. /tmp에 복사해 두고 돌리던 예전 방식은
# 원본과 다른 파일을 렌더하는 사고를 부른다.
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
VERSION = open(os.path.join(ROOT, "VERSION"), encoding="utf-8").read().strip()
HTML = os.path.join(HERE, "manual.html")
OUT = os.path.join(HERE, f"LOST_SURVIVOR_생존안내서_v{VERSION}.pdf")

FOOT = ('<div style="width:100%;font-size:7pt;color:#8a949c;padding:0 16mm;'
        'font-family:\'Noto Sans CJK KR\',sans-serif;display:flex;justify-content:space-between;">'
        '<span>LOST SURVIVOR 절해고도 — 생존 안내서</span>'
        '<span class="pageNumber"></span>'
        '</div>')

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await b.new_page()
        await pg.goto("file://" + HTML, wait_until="networkidle")
        await pg.emulate_media(media="print")
        await pg.pdf(path=OUT,
                     format="A4", print_background=True,
                     display_header_footer=True,
                     header_template='<div></div>', footer_template=FOOT,
                     margin={"top":"18mm","bottom":"20mm","left":"16mm","right":"16mm"})
        await b.close()
        print(OUT)

asyncio.run(main())
