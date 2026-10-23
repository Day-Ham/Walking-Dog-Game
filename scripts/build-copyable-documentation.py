"""Create a browser document with rich-text copying for Google Docs."""
from pathlib import Path
import re
from html import escape

root = Path(__file__).resolve().parents[1]
source = root / 'docs/system-documentation.md'
destination = root / 'output/documents/walking-dog-google-docs-copy.html'
destination.parent.mkdir(parents=True, exist_ok=True)

def inline(text):
    text = escape(text)
    return re.sub(r'`([^`]+)`', r'<code style="font-family:Consolas,monospace;font-size:0.95em">\1</code>', text)

lines = source.read_text(encoding='utf-8').splitlines()
parts = []
i = 0
while i < len(lines):
    line = lines[i]
    if not line.strip():
        i += 1
        continue
    if line == '[[architecture]]':
        parts.append('<ul style="margin:8px 0 14px;padding-left:24px">' + ''.join(
            f'<li style="margin-bottom:6px">{escape(s)}</li>' for s in [
                'Android steps and GPS → Walk manager → Local records',
                'Walk manager → Firebase synchronization → Firestore',
                'Unity feature screens → Account services → Firestore',
                'Firestore room roster and seed → Local race simulation → Race display',
            ]) + '</ul>')
        i += 1
        continue
    if line.startswith('```'):
        code = []
        i += 1
        while i < len(lines) and not lines[i].startswith('```'):
            code.append(lines[i]); i += 1
        parts.append('<pre style="white-space:pre-wrap;font-family:Consolas,monospace;font-size:10pt;line-height:1.4">' + escape('\n'.join(code)) + '</pre>')
        i += 1
        continue
    if line.startswith('|'):
        rows = []
        while i < len(lines) and lines[i].startswith('|'):
            cells = [s.strip() for s in lines[i].strip().strip('|').split('|')]
            if not all(re.fullmatch(r':?-+:?', c) for c in cells):
                rows.append(cells)
            i += 1
        table = ['<table style="border-collapse:collapse;border:1px solid #555;width:100%;margin:8px 0 16px;font-family:Arial,sans-serif;font-size:10pt;table-layout:fixed">']
        for n, row in enumerate(rows):
            table.append('<tr>')
            for cell in row:
                tag = 'th' if n == 0 else 'td'
                shade = 'background-color:#eeeeee;font-weight:bold;' if n == 0 else ''
                table.append(f'<{tag} style="border:1px solid #555;padding:7px 9px;text-align:left;vertical-align:top;overflow-wrap:anywhere;{shade}">{inline(cell)}</{tag}>')
            table.append('</tr>')
        table.append('</table>')
        parts.append(''.join(table))
        continue
    heading = re.match(r'^(#{1,3}) (.*)', line)
    if heading:
        level = len(heading.group(1))
        size = {1:22, 2:16, 3:12}[level]
        parts.append(f'<h{level} style="font-family:Arial,sans-serif;color:#000;font-size:{size}pt;font-weight:bold;margin:22px 0 10px;line-height:1.25">{inline(heading.group(2))}</h{level}>')
        i += 1
        continue
    if re.match(r'^\d+\. ', line):
        items = []
        while i < len(lines) and re.match(r'^\d+\. ', lines[i]):
            items.append('<li style="margin-bottom:6px">' + inline(re.sub(r'^\d+\. ', '', lines[i])) + '</li>')
            i += 1
        parts.append('<ol style="margin:8px 0 14px;padding-left:24px">' + ''.join(items) + '</ol>')
        continue
    para = [line]
    while i+1 < len(lines) and lines[i+1].strip() and not re.match(r'[#|]|\d+\. |```|\[\[', lines[i+1]):
        i += 1; para.append(lines[i])
    parts.append('<p style="margin:0 0 11px;line-height:1.45">' + inline(' '.join(para)) + '</p>')
    i += 1

html = '''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Walking Dog documentation for Google Docs</title>
<style>
body{margin:0;background:#f3f3f3;color:#111;font-family:Arial,sans-serif}
.tools{position:sticky;top:0;background:#fff;border-bottom:1px solid #ccc;padding:14px 24px;display:flex;align-items:center;gap:18px;flex-wrap:wrap;z-index:1}
button{background:#165b39;color:white;border:0;border-radius:5px;padding:12px 18px;font-size:15px;cursor:pointer}
button:focus-visible{outline:3px solid #315ea8;outline-offset:3px}
#status{font-size:14px;line-height:1.4;max-width:760px}
article{max-width:850px;margin:24px auto;background:white;padding:48px;font-size:11pt;font-family:Arial,sans-serif;box-shadow:0 1px 5px #ccc}
@media(max-width:650px){article{padding:20px;margin:0}table{font-size:9pt!important}th,td{padding:5px!important}}
@media print{.tools{display:none}body{background:white}article{box-shadow:none;margin:0;padding:0;max-width:none}h1,h2,h3{break-after:avoid}tr{break-inside:avoid}thead{display:table-header-group}}
</style></head><body>
<div class="tools"><button id="copy" type="button">Copy document</button>
<span id="status" role="status" aria-live="polite">Click Copy document, then paste into Google Docs using Ctrl+V. Use normal paste to keep tables and headings.</span></div>
<article id="document" style="font-family:Arial,sans-serif;font-size:11pt;color:#000">''' + '\n'.join(parts) + '''</article>
<script>
const article=document.getElementById('document');
const status=document.getElementById('status');
document.getElementById('copy').addEventListener('click',async()=>{
  const html='<html><body>'+article.outerHTML+'</body></html>';
  try{
    if(!navigator.clipboard || typeof ClipboardItem==='undefined')throw new Error('fallback');
    await navigator.clipboard.write([new ClipboardItem({
      'text/html':new Blob([html],{type:'text/html'}),
      'text/plain':new Blob([article.innerText],{type:'text/plain'})
    })]);
    status.textContent='Copied. Paste into Google Docs with Ctrl+V to keep formatting.';
  }catch(e){
    const range=document.createRange();range.selectNodeContents(article);
    const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);
    let copied=false;try{copied=document.execCommand('copy')}catch(e){}
    status.textContent=copied?'Copied. Paste into Google Docs with Ctrl+V.':'Document selected. Press Ctrl+C, then paste into Google Docs with Ctrl+V.';
  }
});
</script></body></html>'''
destination.write_text(html, encoding='utf-8')
print(destination)
