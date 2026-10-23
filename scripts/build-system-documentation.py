"""Build the template-style system manual from its editable Markdown source."""
from pathlib import Path
import re
from html import escape

from reportlab.pdfgen import canvas
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    Preformatted, Flowable, KeepTogether,
)
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs/system-documentation.md"
OUTPUT = ROOT / "output/pdf/walking-dog-system-documentation.pdf"
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
FONT_DIR = Path("C:/Windows/Fonts")
for name, filename in [("Manual", "arial.ttf"), ("ManualBold", "arialbd.ttf"),
                       ("ManualItalic", "ariali.ttf"), ("ManualMono", "consola.ttf")]:
    pdfmetrics.registerFont(TTFont(name, str(FONT_DIR / filename)))
pdfmetrics.registerFontFamily("Manual", normal="Manual", bold="ManualBold", italic="ManualItalic")

styles = {
    "title": ParagraphStyle("title", fontName="ManualBold", fontSize=22, leading=26,
                            spaceAfter=14, keepWithNext=True),
    "h2": ParagraphStyle("h2", fontName="ManualBold", fontSize=16, leading=20,
                         spaceBefore=18, spaceAfter=9, keepWithNext=True),
    "h3": ParagraphStyle("h3", fontName="ManualBold", fontSize=11.5, leading=15,
                         spaceBefore=11, spaceAfter=5, keepWithNext=True),
    "body": ParagraphStyle("body", fontName="Manual", fontSize=10.5, leading=14.5,
                           spaceAfter=6, splitLongWords=True),
    "list": ParagraphStyle("list", fontName="Manual", fontSize=10.5, leading=14.2,
                           spaceAfter=4, leftIndent=16, firstLineIndent=-16),
    "cell": ParagraphStyle("cell", fontName="Manual", fontSize=9.5, leading=12.8,
                           spaceAfter=0, splitLongWords=True),
    "cellhead": ParagraphStyle("cellhead", fontName="ManualBold", fontSize=9.5,
                               leading=12.8, spaceAfter=0),
    "code": ParagraphStyle("code", fontName="ManualMono", fontSize=9, leading=12.5,
                           leftIndent=8, spaceBefore=6, spaceAfter=8),
}

def inline(text):
    text = escape(text)
    text = re.sub(r"`([^`]+)`", r'<font name="ManualMono">\1</font>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", text)
    return text

class Architecture(Flowable):
    def __init__(self):
        super().__init__()
        self.width, self.height = 490, 164

    def draw(self):
        c = self.canv
        c.setStrokeColor(colors.HexColor("#555555"))
        c.setFillColor(colors.black)
        c.setLineWidth(.65)
        def box(x, y, label):
            c.rect(x, y, 145, 34)
            c.setFont("Manual", 9)
            c.drawCentredString(x + 72.5, y + 14, label)
        def arrow(x1, y1, x2, y2):
            c.line(x1, y1, x2, y2)
            if x2 > x1:
                c.line(x2, y2, x2-4, y2+2.5)
                c.line(x2, y2, x2-4, y2-2.5)
            else:
                c.line(x2, y2, x2-2.5, y2+4)
                c.line(x2, y2, x2+2.5, y2+4)
        for y, labels in [(122, ["Android steps and GPS", "Walk manager", "Local records"]),
                          (66, ["Unity feature screens", "Account services", "Firestore and rules"]),
                          (10, ["Shared room inputs", "Local race simulation", "Race display"])]:
            for x, label in zip([0, 171, 342], labels):
                box(x, y, label)
            arrow(145, y+17, 171, y+17)
            arrow(316, y+17, 342, y+17)
        arrow(243.5, 122, 243.5, 100)
        c.setFont("Manual", 8)
        c.drawString(251, 110, "sync")
        c.line(414.5, 66, 414.5, 53)
        c.line(414.5, 53, 72.5, 53)
        arrow(72.5, 53, 72.5, 44)

class ManualDoc(SimpleDocTemplate):
    def afterFlowable(self, flowable):
        if isinstance(flowable, Paragraph) and flowable.style.name == "h2":
            label = flowable.getPlainText()
            key = "section-" + label.split()[0]
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(label, key, 0, False)

def page_frame(c, doc):
    c.saveState()
    c.setFont("Manual", 9)
    c.setFillColor(colors.HexColor("#555555"))
    c.drawString(52, A4[1]-34, "Walking Dog")
    c.setFillColor(colors.black)
    c.drawRightString(A4[0]-52, 28, f"pg. {doc.page}")
    c.restoreState()

lines = SOURCE.read_text(encoding="utf-8").splitlines()
story = []
i = 0
while i < len(lines):
    line = lines[i]
    if not line.strip():
        i += 1
        continue
    if line == "[[architecture]]":
        story.extend([Architecture(), Spacer(1, 8)])
        i += 1
        continue
    if line.startswith("```"):
        i += 1
        code = []
        while i < len(lines) and not lines[i].startswith("```"):
            code.append(lines[i]); i += 1
        story.append(Preformatted("\n".join(code), styles["code"]))
        i += 1
        continue
    if line.startswith("|"):
        rows = []
        while i < len(lines) and lines[i].startswith("|"):
            raw = [c.strip() for c in lines[i].strip().strip("|").split("|")]
            if not all(re.fullmatch(r":?-+:?", c) for c in raw):
                rows.append(raw)
            i += 1
        width = A4[0]-104
        ratios = [0.43, 0.57] if len(rows[0]) == 2 else [0.28, 0.30, 0.42]
        if rows[0] == ["Check", "Evidence to record", "Current status"]:
            ratios = [0.25, 0.48, 0.27]
        data = []
        for n, row in enumerate(rows):
            cells = []
            for col, cell in enumerate(row):
                rendered = inline(cell)
                if n and col == 0 and rows[0][0] == "Method" and len(cell) > 43 and "." in cell and not cell.startswith("SetGpsLocation"):
                    match = re.match(r"([A-Za-z]+\.)", cell)
                    if match:
                        rendered = rendered.replace(match.group(1), match.group(1) + "<br/>", 1)
                cells.append(Paragraph(rendered, styles["cellhead" if n == 0 else "cell"]))
            data.append(cells)
        table = Table(data, colWidths=[width*r for r in ratios], repeatRows=1, hAlign="LEFT")
        table.setStyle(TableStyle([
            ("GRID", (0,0), (-1,-1), .45, colors.HexColor("#555555")),
            ("BACKGROUND", (0,0), (-1,0), colors.HexColor("#EFEFEF")),
            ("VALIGN", (0,0), (-1,-1), "TOP"),
            ("TOPPADDING", (0,0), (-1,-1), 5),
            ("BOTTOMPADDING", (0,0), (-1,-1), 5),
            ("LEFTPADDING", (0,0), (-1,-1), 6),
            ("RIGHTPADDING", (0,0), (-1,-1), 6),
        ]))
        story.extend([table, Spacer(1, 7)])
        continue
    if line.startswith("# "):
        story.append(Paragraph(inline(line[2:]), styles["title"]))
    elif line.startswith("## "):
        story.append(Paragraph(inline(line[3:]), styles["h2"]))
    elif line.startswith("### "):
        story.append(Paragraph(inline(line[4:]), styles["h3"]))
    elif re.match(r"\d+\. ", line):
        story.append(Paragraph(inline(line), styles["list"]))
    else:
        para = [line]
        while i+1 < len(lines) and lines[i+1].strip() and not re.match(r"[#|]|\d+\. |```|\[\[", lines[i+1]):
            i += 1; para.append(lines[i])
        story.append(Paragraph(inline(" ".join(para)), styles["body"]))
    i += 1

doc = ManualDoc(str(OUTPUT), pagesize=A4, rightMargin=52, leftMargin=52,
                topMargin=59, bottomMargin=49, title="Walking Dog System Documentation",
                author="Walking Dog Project", subject="Developer system handover")
doc.build(story, onFirstPage=page_frame, onLaterPages=page_frame)
print(OUTPUT)
