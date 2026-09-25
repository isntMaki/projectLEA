import json, subprocess, sys

EDSDK = r"C:\Users\bib\.workbuddy-ai\plugins\cache\workbuddy-builtin\skill-tencent-local-office-edit\5.6.2-wb.39458645.g35219ed6.h85a557611a02\edsdk.py"
PY = r"C:\Users\bib\.workbuddy-ai\binaries\python\versions\3.13.12\python.exe"
FID = "new_doc_9492196570900_fef3"


def call(tool, payload):
    p = subprocess.run([PY, EDSDK, "call", tool, "--json", json.dumps(payload)],
                       capture_output=True, text=True, encoding="utf-8")
    out = p.stdout.strip()
    i = out.find("{")
    if i < 0:
        print("FAIL", tool, "->", out[:300], "| stderr:", p.stderr[:200]); sys.exit(1)
    d = json.loads(out[i:out.rfind("}") + 1])
    if d.get("ok") is False:
        print("FAIL", tool, "->", str(d)[:300]); sys.exit(1)
    print("OK", tool, "->", out[-150:])
    return d


# 1. remove the stale empty TOC paragraph, re-insert so it collects the body headings
r = call("doc_find", {"file_id": FID, "text": "未找到目录项"})
loc = (r.get("locations") or [{}])[0]
print("toc placeholder at:", loc)
if loc:
    call("doc_delete_paragraph", {"file_id": FID, "idx": loc["begin"]})
    r = call("doc_find", {"file_id": FID, "text": "Inhaltsverzeichnis"})
    title_end = (r.get("locations") or [{}])[0]["end"]
    # insert right after the "Inhaltsverzeichnis" title paragraph separator
    call("doc_insert_toc", {"file_id": FID, "idx": title_end + 1, "max_level": 3})
