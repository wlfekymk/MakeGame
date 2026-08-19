import asyncio
from playwright.async_api import async_playwright

FOOT = ('<div style="width:100%;font-size:7pt;color:#8a949c;padding:0 16mm;'
        'font-family:\'Noto Sans CJK KR\',sans-serif;display:flex;justify-content:space-between;">'
        '<span>LOST SURVIVOR 절해고도 — 생존 안내서</span>'
        '<span class="pageNumber"></span>'
        '</div>')

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await b.new_page()
        await pg.goto("file:///tmp/manual/manual.html", wait_until="networkidle")
        await pg.emulate_media(media="print")
        await pg.pdf(path="/tmp/manual/LOST_SURVIVOR_생존안내서_v0.2.54.pdf",
                     format="A4", print_background=True,
                     display_header_footer=True,
                     header_template='<div></div>', footer_template=FOOT,
                     margin={"top":"18mm","bottom":"20mm","left":"16mm","right":"16mm"})
        await b.close()

asyncio.run(main())
