import json, subprocess

EDSDK = r"C:\Users\bib\.workbuddy-ai\plugins\cache\workbuddy-builtin\skill-tencent-local-office-edit\5.6.2-wb.39458645.g35219ed6.h85a557611a02\edsdk.py"
PY = r"C:\Users\bib\.workbuddy-ai\binaries\python\versions\3.13.12\python.exe"
FID = "new_doc_9492196570900_fef3"
STYLE = {1: "c3ru9c", 2: "hlutld", 3: "cfvhdp"}

HEADINGS = [
    (1, "1 Einleitung"),
    (2, "1.1 Abstract"),
    (2, "1.2 Aufgabenstellung und Ziel"),
    (2, "1.3 Zielgruppenanalyse"),
    (1, "2 Hauptteil"),
    (2, "2.1 Software / Hardware"),
    (3, "2.1.1 Einrichten der Entwicklungsumgebung"),
    (3, "2.1.2 Produktbeschreibung"),
    (3, "2.1.3 Model"),
    (3, "2.1.4 View (GUI)"),
    (3, "2.1.5 Control"),
    (3, "2.1.6 Problemlösungen"),
    (3, "2.1.7 Hardware"),
    (2, "2.2 Medienprojekt"),
    (3, "2.2.1 Konkurrenzanalyse"),
    (3, "2.2.2 Ideenfindung und Kreativprozess"),
    (2, "2.3 Verlauf des Projekts"),
    (2, "2.4 Zusammenfassung und Ausblick"),
    (2, "2.5 Literaturverzeichnis"),
    (1, "3 Erklärung"),
]


def call(tool, payload):
    p = subprocess.run([PY, EDSDK, "call", tool, "--json", json.dumps(payload)],
                       capture_output=True, text=True, encoding="utf-8")
    out = p.stdout.strip()
    i = out.find("{")
    d = json.loads(out[i:out.rfind("}") + 1]) if i >= 0 else {}
    return d, (str(d.get("error", ""))[:140] if d.get("ok") is False else None)


fails = 0
for level, text in HEADINGS:
    r, err = call("doc_find", {"file_id": FID, "text": text})
    if err:
        print("FIND FAIL", text, err); fails += 1; continue
    locs = r.get("locations") or []
    if len(locs) != 1:
        print("AMBIGUOUS", text, len(locs)); fails += 1; continue
    loc = locs[0]
    r, err = call("doc_apply_named_style",
                  {"file_id": FID, "ranges": [{"begin": loc["begin"], "end": loc["end"]}],
                   "target_style_id": STYLE[level]})
    if err:
        print("STYLE FAIL", text, err); fails += 1
    else:
        print("OK  H%d %s" % (level, text[:45]))
print("fails:", fails)
