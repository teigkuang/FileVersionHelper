# -*- coding: utf-8 -*-
"""把 用户手册-v0.1.md 生成响应式电子说明书（单文件 HTML）。

用法：python build_site.py
改文案 → 改上级目录的 Markdown，再跑一次本脚本
改版式 → 改 template.html
产物 FVHelper用户手册.html 内联了全部截图，可单独发给别人。
"""
import base64
import html
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
MD = HERE.parent / "用户手册-v0.1.md"
TPL = HERE / "template.html"
OUT = HERE / "FVHelper用户手册.html"
ASSETS = HERE / "assets"

IMG_MAP = {
    "主界面-打开文件目录按钮.png": "ui-main.png",
    "提交确认窗-标题与详情.png": "commit-dialog.png",
    "提交详情-版本详细信息.png": "detail-versions.png",
    "含数据导出对话框.png": "export-meta-dialog.png",
    "P3-导出完成页.png": "export-done.png",
    "设置窗口-版本信息署名.png": "settings.png",
}

PARTS = [
    ("开始使用", [1, 2, 3]),
    ("日常操作", [4, 5, 6, 7]),
    ("看历史与分享", [8, 9]),
    ("校验与设置", [10, 11, 12]),
    ("数据与排错", [13, 14]),
    ("进阶与附录", [15, 16, 17]),
]

STATUS = {
    "修改": "st-mod", "新增": "st-new", "删除": "st-del", "改名": "st-ren", "无变化": "st-same",
    "绿": "st-ok", "橙": "st-mod", "红": "st-bad", "蓝": "st-ren", "灰": "st-same",
}

IMG_COUNTER = [0]


def esc(t):
    return html.escape(t, quote=False)


def inline(text):
    spans = []

    def hold(m):
        spans.append("<code>" + esc(m.group(1)) + "</code>")
        return "\x00%d\x00" % (len(spans) - 1)

    text = re.sub(r"`([^`]+)`", hold, text)
    text = esc(text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(
        r"\[(.+?)\]\((https?://[^\s)]+)\)",
        lambda m: '<a href="%s" target="_blank" rel="noreferrer">%s</a>' % (m.group(2), m.group(1)),
        text,
    )

    def sec(m):
        ch, sub = m.group(1), m.group(2)
        anchor = "ch%s" % ch if not sub else "ch%s-%s" % (ch, sub)
        return '<a href="#%s">§%s%s</a>' % (anchor, ch, ("." + sub) if sub else "")

    text = re.sub(r"§(\d+)(?:\.(\d+))?", sec, text)
    for i, s in enumerate(spans):
        text = text.replace("\x00%d\x00" % i, s)
    return text


def col_widths(n):
    presets = {
        2: [30, 70], 3: [22, 34, 44], 4: [22, 26, 26, 26],
        5: [9, 31, 14, 20, 26], 6: [8, 22, 12, 18, 18, 22],
    }
    return presets.get(n, [round(100 / n, 2)] * n)


def table(rows):
    head, body = rows[0], rows[2:]
    n = max(len(r) for r in rows)
    cg = "".join('<col style="width:%s%%">' % w for w in col_widths(n))
    minw = 460 if n >= 4 else (400 if n == 3 else 340)
    out = ['<div class="tbl-wrap"><table style="min-width:%dpx">' % minw, "<colgroup>%s</colgroup><thead><tr>" % cg]
    for c in head:
        out.append("<th>%s</th>" % inline(c))
    out.append("</tr></thead><tbody>")
    for r in body:
        out.append("<tr>")
        for i, c in enumerate(r):
            cls = STATUS.get(c.strip()) if i > 0 else None
            cell = '<span class="st %s">%s</span>' % (cls, inline(c)) if cls else inline(c)
            out.append("<td>%s</td>" % cell)
        out.append("</tr>")
    out.append("</tbody></table></div>")
    return "".join(out)


def parse_list(lines, start):
    base = len(lines[start]) - len(lines[start].lstrip())
    ordered = bool(re.match(r"\s*\d+\.", lines[start]))
    items, i = [], start
    while i < len(lines):
        raw = lines[i]
        if not raw.strip():
            break
        indent = len(raw) - len(raw.lstrip())
        m = re.match(r"\s*(?:-|\d+\.)\s+(.*)$", raw)
        if not m or indent < base:
            break
        if indent == base:
            items.append([m.group(1), []])
            i += 1
        else:
            j = i
            while j < len(lines) and lines[j].strip() and (len(lines[j]) - len(lines[j].lstrip())) > base:
                items[-1][1].append(lines[j])
                j += 1
            i = j

    def render_sub(sub_lines):
        sub_ordered = bool(re.match(r"\s*\d+\.", sub_lines[0]))
        tag = "ol" if sub_ordered else "ul"
        parts, last = ["<%s>" % tag], None
        for s in sub_lines:
            m = re.match(r"\s*(?:-|\d+\.)\s+(.*)$", s)
            if m:
                if last is not None:
                    parts.append("</li>")
                parts.append("<li>%s" % inline(m.group(1)))
                last = True
            elif last:
                parts.append(inline(s.strip()))
        if last:
            parts.append("</li>")
        parts.append("</%s>" % tag)
        return "".join(parts)

    parts = ["<%s>" % ("ol" if ordered else "ul")]
    for text, subs in items:
        parts.append("<li>%s" % inline(text))
        if subs:
            parts.append(render_sub(subs))
        parts.append("</li>")
    parts.append("</%s>" % ("ol" if ordered else "ul"))
    return "".join(parts), i


def image_fig(alt, src):
    name = src.split("/")[-1]
    asset = IMG_MAP.get(name)
    if not asset:
        return "<p><em>（缺图：%s）</em></p>" % esc(name)
    data = base64.b64encode((ASSETS / asset).read_bytes()).decode("ascii")
    IMG_COUNTER[0] += 1
    n = IMG_COUNTER[0]
    lazy = ' loading="lazy" decoding="async"' if n > 1 else ' decoding="async"'
    return (
        '<figure><div class="fig-img"><img src="data:image/png;base64,%s" alt="%s"%s></div>'
        "<figcaption><i>图 %d</i>%s</figcaption></figure>" % (data, esc(alt), lazy, n, esc(alt))
    )


def convert(md_text):
    lines = md_text.splitlines()
    start = 0
    for idx, ln in enumerate(lines):
        if re.match(r"^## \d+\.", ln):
            start = idx
            break
    lines = lines[start:]

    chapters, cur, buf = [], None, []
    i = 0
    while i < len(lines):
        raw = lines[i]
        s = raw.strip()
        if not s or s == "---":
            i += 1
            continue

        m = re.match(r"^## (\d+)\. (.+)$", s)
        if m:
            if cur:
                cur["chunks"] = buf
                chapters.append(cur)
            cur, buf = {"num": int(m.group(1)), "title": m.group(2), "subs": []}, []
            i += 1
            continue

        m = re.match(r"^### (\d+)\.(\d+) (.+)$", s)
        if m:
            ch, sub, title = m.group(1), m.group(2), m.group(3)
            cur["subs"].append((sub, title))
            buf.append('<h3 id="ch%s-%s"><span class="sn">%s.%s</span>%s</h3>' % (ch, sub, ch, sub, inline(title)))
            i += 1
            continue

        if s.startswith("```"):
            i += 1
            code = []
            while i < len(lines) and not lines[i].strip().startswith("```"):
                code.append(lines[i])
                i += 1
            i += 1
            buf.append("<pre><code>%s</code></pre>" % esc("\n".join(code)))
            continue

        if s.startswith(">"):
            quote = []
            while i < len(lines) and lines[i].strip().startswith(">"):
                quote.append(re.sub(r"^>\s?", "", lines[i].strip()))
                i += 1
            buf.append("<blockquote><p>%s</p></blockquote>" % "\n".join(inline(q) for q in quote))
            continue

        if s.startswith("|") and i + 1 < len(lines) and re.match(r"\|[\s:|-]+\|", lines[i + 1].strip()):
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            buf.append(table(rows))
            continue

        m = re.match(r"^!\[(.+?)\]\(([^)]+)\)$", s)
        if m:
            buf.append(image_fig(m.group(1), m.group(2)))
            i += 1
            continue

        if re.match(r"(?:-|\d+\.)\s+", raw):
            block, i = parse_list(lines, i)
            buf.append(block)
            continue

        para = [s]
        i += 1
        while i < len(lines):
            nxt = lines[i]
            ns = nxt.strip()
            if (not ns or re.match(r"^(##|###|\||>|```|!\[|---)", ns)
                    or re.match(r"(?:-|\d+\.)\s+", nxt) or ns.startswith("**Q")):
                break
            para.append(ns)
            i += 1
        text = inline(" ".join(para))
        cls = ' class="qa-q"' if " ".join(para).startswith("**Q") else ""
        buf.append("<p%s>%s</p>" % (cls, text))

    if cur:
        cur["chunks"] = buf
        chapters.append(cur)
    return chapters


def assemble(chapters):
    out = []
    for idx, ch in enumerate(chapters):
        out.append('<section class="ch" id="ch%d">' % ch["num"])
        out.append('<header class="ch-head"><span class="ch-num">%02d</span><h2>%s</h2></header>'
                   % (ch["num"], inline(ch["title"])))
        out.append("".join(ch["chunks"]))
        nav = []
        if idx > 0:
            p = chapters[idx - 1]
            nav.append('<a href="#ch%d"><small>上一章</small><b>%d. %s</b></a>' % (p["num"], p["num"], esc(p["title"])))
        if idx < len(chapters) - 1:
            n = chapters[idx + 1]
            nav.append('<a href="#ch%d"><small>下一章</small><b>%d. %s</b></a>' % (n["num"], n["num"], esc(n["title"])))
        out.append('<div class="chp">%s</div></section>' % "".join(nav))
    return "".join(out)


def build_nav(chapters):
    by_num = {c["num"]: c for c in chapters}
    out = []
    for part, nums in PARTS:
        out.append('<div class="part"><div class="part-title">%s</div><ol class="ch-list">' % part)
        for n in nums:
            ch = by_num.get(n)
            if not ch:
                continue
            hay = (str(n) + " " + ch["title"] + " " + " ".join(t for _, t in ch["subs"])).lower()
            out.append('<li class="ch-item" data-search="%s"><a class="ch-link" href="#ch%d"><span class="n">%02d</span>%s</a>'
                       % (esc(hay), n, n, inline(ch["title"])))
            if ch["subs"]:
                out.append('<ol class="subs">')
                for sub, title in ch["subs"]:
                    out.append('<li><a class="sub-link" href="#ch%d-%s">%s.%s %s</a></li>' % (n, sub, n, sub, inline(title)))
                out.append("</ol>")
            out.append("</li>")
        out.append("</ol></div>")
    return "\n".join(out)


def main():
    md = MD.read_text(encoding="utf-8")
    chapters = convert(md)
    version = re.search(r"\| 软件版本 \| (v[^ |]+) \|", md)
    date = re.search(r"\| 构建时间 \| ([\d-]+) ", md)
    tpl = TPL.read_text(encoding="utf-8")
    out = (tpl.replace("{{NAV}}", build_nav(chapters))
              .replace("{{CONTENT}}", assemble(chapters))
              .replace("{{META_VERSION}}", version.group(1) if version else "v0.1-preview")
              .replace("{{META_DATE}}", date.group(1) if date else "2026-11-06"))
    OUT.write_text(out, encoding="utf-8")
    print("chapters=%d sections=%d nav=%d subs=%d images=%d size=%dKB"
          % (len(chapters), out.count('<section class="ch"'), out.count("ch-link"),
             out.count("sub-link"), IMG_COUNTER[0], OUT.stat().st_size // 1024))


if __name__ == "__main__":
    main()
