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
    print("OK", tool, "->", out[-160:])
    return d


# --- 1. locate cover title paragraph, insert title image before it ---
r = call("doc_find", {"file_id": FID, "text": "Shoot a Bean"})
hits = r.get("locations") or r.get("matches") or r.get("results") or r.get("items") or []
print("find hits:", json.dumps(hits, ensure_ascii=False)[:400])
begin = hits[0]["begin"] if hits else r.get("begin")
# create an empty paragraph right before the cover title, then drop the image into it
spacer = call("doc_insert_paragraph_with_text",
              {"file_id": FID, "idx": begin, "text": "",
               "paragraph_property": {"jc": "center"}})
img_idx = spacer["range"]["begin"]
r = call("doc_insert_image", {"file_id": FID, "idx": img_idx,
                              "image_path": r"C:\Dev\projectLEA\Assets\Resources\TitleCard.jpg",
                              "w": 560, "h": 280})

# --- 2. page break, then TOC ---
last = call("doc_get_last_operable_pos", {"file_id": FID})
pb1 = call("doc_insert_page_break", {"file_id": FID, "idx": last["position"]})
toc_title = call("doc_insert_paragraph_with_text",
                 {"file_id": FID, "idx": pb1.get("next_index", pb1.get("last_edit_index")),
                  "text": "Inhaltsverzeichnis",
                  "text_format": {"font_family": "Arial", "font_size": 16, "bold": True},
                  "paragraph_property": {"jc": "center"}})
toc = call("doc_insert_toc", {"file_id": FID, "idx": toc_title["next_index"], "max_level": 3})
pb2_idx = toc.get("last_edit_index") or toc.get("position") or toc_title["next_index"]
pb2 = call("doc_insert_page_break", {"file_id": FID, "idx": pb2_idx + 1})

# --- 3. body markdown ---
body_idx = pb2.get("next_index") or (pb2.get("last_edit_index", 0) + 1)
call("doc_insert_markdown", {"file_id": FID, "idx": body_idx,
                             "markdown": "file://C:/Dev/projectLEA/Tools/build_doc.md"})
print("BODY INSERTED")
