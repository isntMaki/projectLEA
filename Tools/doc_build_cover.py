import json, subprocess, sys

EDSDK = r"C:\Users\bib\.workbuddy-ai\plugins\cache\workbuddy-builtin\skill-tencent-local-office-edit\5.6.2-wb.39458645.g35219ed6.h85a557611a02\edsdk.py"
PY = r"C:\Users\bib\.workbuddy-ai\binaries\python\versions\3.13.12\python.exe"
FID = "new_doc_9492196570900_fef3"


def call(tool, payload):
    p = subprocess.run([PY, EDSDK, "call", tool, "--json", json.dumps(payload)],
                       capture_output=True, text=True, encoding="utf-8")
    out = p.stdout.strip()
    # tool prints the JSON-RPC result twice (once raw, once wrapped) - take the last {..}
    i = out.find("{")
    if i < 0:
        print("FAIL", tool, out, p.stderr); sys.exit(1)
    last = out.rfind("}")
    d = json.loads(out[i:last + 1])
    if not d.get("ok", True) if isinstance(d.get("ok"), bool) else False:
        pass
    print(tool, "->", out[-200:])
    return d


res = {}

# cover paragraphs after the first one (next_index chains from 18)
paras = [
    ("", None, None),                                   # spacer
    ("Shoot a Bean", 36, True),
    ("Arena Combat", 18, False),
    ("", None, None),
    ("Projektdokumentation", 13, False),
    ("", None, None),
    ("Emmanuel Johnson", 13, True),
    ("Tahsin Can Gördesli", 13, True),
    ("", None, None),
    ("Paderborn, den 25. September 2026", 12, False),
]
idx = 18
for text, size, bold in paras:
    tf = {"font_family": "Arial"}
    if size:
        tf["font_size"] = size
    if bold:
        tf["bold"] = True
    r = call("doc_insert_paragraph_with_text",
             {"file_id": FID, "idx": idx, "text": text, "text_format": tf,
              "paragraph_property": {"jc": "center"}})
    idx = r["next_index"]

print("cover done, next_index", idx)
