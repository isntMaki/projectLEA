import re, sys, pathlib

scene = pathlib.Path(r"C:\Dev\projectLEA\Assets\Scenes\Manuel.unity")
txt = scene.read_text(encoding="utf-8", errors="replace")

print("=== Manuel scene verification ===")
print("file size:", len(txt))

# Collect all script GUID references used by MonoBehaviours
guids = set(re.findall(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", txt))
known = {
    "7a1b3c5d9e0f4a2b8c6d1e3f5a7b9c0d": "PlayerController",
    "aa11bb22cc33dd44ee55ff6600112233": "DamageDummy",
    "bb22cc33dd44ee55ff66001122334455": "Billboard",
    "cc33dd44ee55ff660011223344556677": "WeaponController",
}
for g in sorted(guids):
    print("  script guid", g, "->", known.get(g, "<unknown/package>"))

# Object names
names = re.findall(r"^  m_Name: (.+)$", txt, re.M)
print("\n=== Named objects ===")
for n in names:
    print("  ", n)

print("\n=== Key presence ===")
for key in ["WeaponHolder", "DamageDummy", "DamageNumber", "FP_Hammer_Rigged",
            "WeaponController", "Animator", "FirstPerson Camera", "Player"]:
    print(f"  {key:24s} {'YES' if key in txt else 'NO'}")
