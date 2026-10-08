"""Bundle index.html into one self-contained file: inline local CSS/JS and embed assets as data URIs."""
import base64, mimetypes, os, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
mimetypes.add_type("image/webp", ".webp")

def data_uri(path):
    p = os.path.join(ROOT, path)
    if not os.path.isfile(p): return None
    mt = mimetypes.guess_type(p)[0] or "application/octet-stream"
    return f"data:{mt};base64," + base64.b64encode(open(p, "rb").read()).decode()

def fix_urls(text, base):
    def u(m):
        ref = m.group(2)
        if ref.startswith(("data:", "http", "#")): return m.group(0)
        d = data_uri(os.path.normpath(os.path.join(base, ref)))
        return f"url({m.group(1)}{d}{m.group(1)})" if d else m.group(0)
    return re.sub(r"url\((['\"]?)([^'\")]+)\1\)", u, text)

html = open(os.path.join(ROOT, "index.html"), encoding="utf-8").read()
html = re.sub(r'<link rel="stylesheet" href="(css/[^"]+)">',
              lambda m: "<style>" + fix_urls(open(os.path.join(ROOT, m.group(1)), encoding="utf-8").read(), os.path.dirname(m.group(1))) + "</style>", html)
# Inline every script (vendor libs from tools/vendor, then site JS) as plain <script> blocks.
# Artifact sandboxes can block CDN and data: script URLs; the tags sit at the end of <body>, so order and DOM access hold.
VENDOR = {"gsap.min.js": "gsap.min.js", "ScrollTrigger.min.js": "ScrollTrigger.min.js",
          "lenis.min.js": "lenis.min.js", "index.min.js": "split-type.min.js"}
def inline_js(m):
    src = m.group(1)
    path = os.path.join(ROOT, "tools", "vendor", VENDOR[src.rsplit("/", 1)[-1]]) if src.startswith("http") else os.path.join(ROOT, src)
    code = open(path, encoding="utf-8").read().replace("</script", "<\\/script")
    return "<script>" + code + "\n</script>"
html = re.sub(r'<script defer src="([^"]+)"></script>', inline_js, html)
html = re.sub(r'((?:href|src)=")(assets/[^"]+)"', lambda m: m.group(1) + (data_uri(m.group(2)) or m.group(2)) + '"', html)
html = fix_urls(html, "")
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "LincolnWC-preview.html")
open(out, "w", encoding="utf-8").write(html)
print(out, len(html) // 1024, "KB")
