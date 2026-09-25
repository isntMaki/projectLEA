"""Structural sanity check for the Manuel combat scripts.

Unity itself is the only real compiler available here, so this script does the
cheap checks that catch the mistakes I am most likely to make:

  1. Brace / paren / bracket balance per file (ignoring strings and comments).
  2. Every `_player.X` reference in PlayerCombat resolves to a public member
     declared in PlayerController.
  3. Every `_combat.X` reference in StaminaBarUI resolves to a public member
     declared in PlayerCombat.
  4. Every type referenced by CombatPrototypeSetup's AddComponent<> calls exists
     as a class in the Manuel namespace.
  5. No leftover TODO / placeholder markers.
  6. No local type declaration shadows an engine type whose static members are in use.
     (A nested `enum Screen` silently reroutes every `Screen.width` to itself, which
     the compiler reports as CS0117 - and this script used to miss it entirely.)
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = ROOT / "Assets" / "Scripts" / "Manuel"

# Discovered at runtime, so scripts can be added or removed without breaking this check.
FILES = {p.stem: p for p in sorted(SCRIPTS.rglob("*.cs"))}

# Engine / package types that AddComponent<T>() may legitimately reference.
ENGINE_TYPES = {
    "Animator", "Animation", "CapsuleCollider", "BoxCollider", "SphereCollider",
    "MeshCollider", "Rigidbody", "CharacterController", "AudioSource", "Light",
    "Camera", "Canvas", "CanvasScaler", "GraphicRaycaster", "Image", "Text",
    "RectTransform", "EventSystem", "StandaloneInputModule", "ParticleSystem",
    "TextMesh", "MeshFilter", "MeshRenderer", "SkinnedMeshRenderer", "SpriteRenderer",
    "LineRenderer", "TrailRenderer", "TerrainCollider", "Cloth", "NavMeshAgent",
    "TextMeshProUGUI", "TextMeshPro", "TMP_Text",
    # Not AddComponent targets, but they occupy the same generic-argument slots.
    "Collider", "Renderer", "Material", "Shader", "Behaviour", "MonoBehaviour",
}

problems: list[str] = []
notes: list[str] = []

# Engine types whose static members get used unqualified (Screen.width, Time.deltaTime...).
# Declaring one of these names anywhere in a file shadows the engine type for the whole file.
ENGINE_STATIC_TYPES = {
    "Screen", "Application", "Input", "Physics", "Physics2D", "Time", "Mathf",
    "QualitySettings", "Cursor", "AudioSettings", "Shader", "Graphics",
    "RenderSettings", "PlayerPrefs", "ScreenCapture", "GL", "GraphicsSettings",
    "PlayerLoop", "Object", "GameObject", "Transform", "Behaviour",
}


def strip_code(src: str) -> str:
    """Remove comments and string/char literals so bracket counting is honest."""
    out = []
    i = 0
    n = len(src)
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""

        # Line comment
        if c == "/" and nxt == "/":
            while i < n and src[i] != "\n":
                i += 1
            continue

        # Block comment
        if c == "/" and nxt == "*":
            i += 2
            while i + 1 < n and not (src[i] == "*" and src[i + 1] == "/"):
                i += 1
            i += 2
            continue

        # Verbatim string
        if c == "@" and nxt == '"':
            i += 2
            while i < n:
                if src[i] == '"':
                    if i + 1 < n and src[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            continue

        # Interpolated / normal string
        if c == '"':
            i += 1
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == '"':
                    i += 1
                    break
                i += 1
            continue

        # Char literal
        if c == "'":
            i += 1
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == "'":
                    i += 1
                    break
                i += 1
            continue

        out.append(c)
        i += 1

    return "".join(out)


def check_balance(name: str, code: str) -> None:
    pairs = {")": "(", "]": "[", "}": "{"}
    stack: list[tuple[str, int]] = []
    line = 1
    for ch in code:
        if ch == "\n":
            line += 1
        elif ch in "([{":
            stack.append((ch, line))
        elif ch in ")]}":
            if not stack:
                problems.append(f"{name}: unmatched '{ch}' on line {line}")
                return
            opener, opened_at = stack.pop()
            if opener != pairs[ch]:
                problems.append(
                    f"{name}: '{ch}' on line {line} closes '{opener}' opened on line {opened_at}"
                )
                return
    if stack:
        opener, opened_at = stack[-1]
        problems.append(f"{name}: '{opener}' opened on line {opened_at} is never closed")
    else:
        notes.append(f"{name}: brackets balanced")


def read(name: str) -> str:
    path = FILES[name]
    if not path.exists():
        problems.append(f"{name}: missing file {path}")
        return ""
    return path.read_text(encoding="utf-8", errors="replace")


# --- 1. balance ------------------------------------------------------------
sources: dict[str, str] = {}
for key in FILES:
    src = read(key)
    sources[key] = src
    if src:
        check_balance(key, strip_code(src))


# --- helper: collect public member names from a class ----------------------
def public_members(src: str) -> set[str]:
    """Names of public properties, fields, methods and events."""
    names: set[str] = set()

    # public <type> Name { ... }   (property / indexer)
    for m in re.finditer(r"\bpublic\s+(?:static\s+|readonly\s+|virtual\s+|override\s+)*"
                         r"[A-Za-z_][\w<>\[\],\.\?]*\s+([A-Za-z_]\w*)\s*\{", src):
        names.add(m.group(1))

    # public <type> Name(...)   (method)
    for m in re.finditer(r"\bpublic\s+(?:static\s+|virtual\s+|override\s+|async\s+)*"
                         r"[A-Za-z_][\w<>\[\],\.\?]*\s+([A-Za-z_]\w*)\s*\(", src):
        names.add(m.group(1))

    # public <type> Name;  or  public <type> Name = ...;   (field)
    for m in re.finditer(r"\bpublic\s+(?:static\s+|readonly\s+)*"
                         r"[A-Za-z_][\w<>\[\],\.\?]*\s+([A-Za-z_]\w*)\s*(?:=|;)", src):
        names.add(m.group(1))

    # public event System.Action Name;
    for m in re.finditer(r"\bpublic\s+event\s+[\w\.<>]+\s+([A-Za-z_]\w*)", src):
        names.add(m.group(1))

    return names


def check_usage(owner: str, field: str, used_src: str, declared_src: str) -> None:
    used = set(re.findall(rf"\b{re.escape(field)}\.([A-Za-z_]\w*)", used_src))
    declared = public_members(declared_src)
    for member in sorted(used):
        if member in declared:
            notes.append(f"{owner}: {field}.{member} OK")
        else:
            problems.append(f"{owner}: {field}.{member} is NOT a public member of the target class")


# --- 2/3. cross-file member references ------------------------------------
def src(name: str) -> str:
    """Source for a script that may or may not exist any more."""
    return sources.get(name, "")


if src("PlayerCombat") and src("PlayerController"):
    check_usage("PlayerCombat", "_player", src("PlayerCombat"), src("PlayerController"))

if src("StaminaBarUI") and src("PlayerCombat"):
    check_usage("StaminaBarUI", "_combat", src("StaminaBarUI"), src("PlayerCombat"))


# --- 4. AddComponent<T>() targets exist ------------------------------------
all_script_text = "\n".join(sources.values())
declared_classes = set(re.findall(r"\bclass\s+([A-Za-z_]\w*)", all_script_text))

def generic_parameters(src: str) -> set:
    """Names introduced as generic type parameters, e.g. 'T' from 'where T : Component'."""
    return set(re.findall(r"\bwhere\s+([A-Za-z_]\w*)\s*:", src))


for owner in sorted(sources):
    generics = generic_parameters(sources[owner])
    for type_name in re.findall(r"AddComponent<([A-Za-z_]\w*)>", sources[owner]):
        if type_name in generics:
            notes.append(f"{owner}: AddComponent<{type_name}> is a generic parameter OK")
        elif type_name in declared_classes or type_name in ENGINE_TYPES:
            notes.append(f"{owner}: AddComponent<{type_name}> OK")
        else:
            problems.append(f"{owner}: AddComponent<{type_name}> has no matching class")


# --- 5. field / helper return-type mismatches ------------------------------
# Catches the mistake that produced CS0029/CS1503 in LoadoutUI: assigning a helper's
# result to a field of the wrong type, e.g. `_moneyText = CreateRect(...)` where
# _moneyText is a Text but CreateRect returns a RectTransform.
def method_return_types(src: str) -> dict:
    types = {}
    pattern = re.compile(
        r"\b(?:private|public|protected|internal)\s+"
        r"(?:static\s+|virtual\s+|override\s+|readonly\s+|async\s+)*"
        r"([A-Za-z_][\w<>\[\],\.]*)\s+([A-Za-z_]\w*)\s*\(")
    for m in pattern.finditer(src):
        types.setdefault(m.group(2), m.group(1))
    return types


def field_types(src: str) -> dict:
    types = {}
    pattern = re.compile(
        r"\b(?:private|public|protected|internal)\s+(?:static\s+|readonly\s+)*"
        r"([A-Za-z_][\w<>\[\],\.]*)\s+([A-Za-z_]\w*)\s*(?:=[^;]*)?;")
    for m in pattern.finditer(src):
        types.setdefault(m.group(2), m.group(1))
    return types


for owner in sorted(sources):
    text = sources[owner]
    returns = method_return_types(text)
    fields = field_types(text)

    for m in re.finditer(r"\b([A-Za-z_]\w*)\s*=\s*([A-Za-z_]\w*)\s*\(", text):
        field, method = m.group(1), m.group(2)
        if field not in fields or method not in returns:
            continue

        declared, actual = fields[field], returns[method]
        if declared == actual or "<" in declared or "<" in actual:
            continue

        problems.append(
            f"{owner}: field '{field}' is {declared} but is assigned {method}() which returns {actual}")


# --- 6. local types shadowing engine types --------------------------------
# Declaring `enum Screen` (or class/struct/interface) makes every `Screen.width` in
# that file resolve to the local type - CS0117 at every use. The safe names are always
# qualified (`UnityEngine.Screen`) or renamed, so a bare declaration is always a bug.
for key, text in sources.items():
    declared_here = set(re.findall(
        r"\b(?:public|private|protected|internal)?\s*(?:static\s+|sealed\s+|abstract\s+)*"
        r"(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)", text))

    shadowed = declared_here & ENGINE_STATIC_TYPES
    for name in sorted(shadowed):
        problems.append(
            f"{key}: declares a type '{name}' that shadows UnityEngine.{name}; "
            f"rename it or qualify every use")


# --- 6b. a type from a sibling namespace used without its using ------------
# C# searches a namespace's ENCLOSING namespaces for a bare name, but never its
# siblings. ProjectLEA.Manuel.Managers can see ProjectLEA.Manuel.PlayerController
# unqualified (it is an ancestor) but NOT ProjectLEA.Manuel.Net.MatchPreset (a
# sibling) - that one needs `using ProjectLEA.Manuel.Net;`, and its absence was a
# real CS0246 that this script used to wave through because it does no type
# resolution. Restricted to PascalCase tokens, because prose words in doc comments
# ('has', 'to', 'roster') collide with nested-type names and drown the signal.
declared_by_ns: dict[str, set[str]] = {}
file_ns: dict[str, str] = {}
file_usings: dict[str, set[str]] = {}
file_decls: dict[str, set[str]] = {}

for key, text in sources.items():
    code = strip_code(text)          # comments and strings gone, so prose cannot match

    ns_match = re.search(r"^\s*namespace\s+([\w.]+)", code, flags=re.M)
    ns = ns_match.group(1) if ns_match else ""
    file_ns[key] = ns

    file_usings[key] = set(re.findall(r"^\s*using\s+([\w.]+)\s*;", code, flags=re.M))

    decls = set(re.findall(r"\b(?:class|struct|enum|interface)\s+([A-Z]\w*)", code))
    file_decls[key] = decls
    declared_by_ns.setdefault(ns, set()).update(decls)

for key, text in sources.items():
    ns = file_ns[key]
    if not ns:
        continue

    code = strip_code(text)          # comments and strings gone, so prose cannot match
    # Drop already-qualified references; those carry their own namespace.
    code = re.sub(r"[\w.]*\.[A-Z]\w*", " ", code)

    for token in set(re.findall(r"\b([A-Z]\w*)\b", code)):
        if token in file_decls[key]:
            continue

        # Only a token that is FOLLOWED by an identifier occupies a type slot
        # (`MatchPreset preset`, `List<Foo> items`). A token followed by ; = ( { , >
        # is a declared NAME (field, method, enum member) - `RectTransform Row;`
        # makes Row a field, not a reference to a type called Row.
        if not re.search(rf"\b{token}\s+[A-Za-z_]", code):
            continue

        for other_ns, names in declared_by_ns.items():
            if not other_ns or other_ns == ns:
                continue
            # C# searches a namespace's enclosing namespaces for a bare name, so an
            # ancestor's types are visible without a using. Siblings are not.
            if ns.startswith(other_ns + ".") or other_ns.startswith(ns + "."):
                continue

            if token in names and other_ns not in file_usings[key]:
                problems.append(
                    f"{key}: uses '{token}' from {other_ns} but has no "
                    f"`using {other_ns};` - CS0246")


# --- 6c. a NAMESPACE named as a qualification prefix that is not reachable ---
# `using A.B;` imports the types inside A.B, not A.B's NAME - so `B.Thing` is a CS0246
# even with the using present, and so is `B.Thing` from a namespace that merely shares
# A as an ancestor. A nested namespace is only nameable as a prefix when it is reachable:
# top-level, or nested inside the file's own namespace or an enclosing one. That is what
# bit us here (`MovementAbilities.DashAbility` from ...Managers with only the parent
# namespace imported). Check 6b cannot see it: it strips every dotted name first, so a
# partially-qualified reference never reaches the token scan. This pass reads the dotted
# names instead.
def declared_namespaces(code: str) -> list[str]:
    """Every namespace declaration in a file, fully qualified, nested ones included.

    Walks braces so a nested `namespace BodyAbilities` inside
    `namespace ProjectLEA.Manuel.Abilities` is returned as its full dotted name.
    """
    out: list[str] = []
    stack: list[tuple[str, int]] = []   # (declared name, brace depth before it opened)
    depth = 0
    for m in re.finditer(r"namespace\s+([\w.]+)\s*(?=[{;])|([{}])", code):
        name, brace = m.group(1), m.group(2)
        if name is not None:
            stack.append((name, depth))
            out.append(".".join(n for n, _ in stack))
        elif brace == "{":
            depth += 1
        else:
            depth -= 1
            while stack and stack[-1][1] >= depth:
                stack.pop()
    return out


ns_leaf: dict[str, set[str]] = {}      # leaf name -> full dotted namespace names
for _key, _text in sources.items():
    for _full in declared_namespaces(strip_code(_text)):
        ns_leaf.setdefault(_full.rsplit(".", 1)[-1], set()).add(_full)

for key, text in sources.items():
    ns = file_ns[key]
    if not ns:
        continue

    # Namespaces nameable as a prefix from here: those nested in this file's own
    # namespace or in any enclosing one (plus the top level, which chain includes as "").
    parts = ns.split(".")
    chain = {".".join(parts[:i]) for i in range(len(parts) + 1)}

    code = strip_code(text)
    for m in re.finditer(r"\b([A-Z]\w*)\.([A-Z]\w*)", code):
        head = m.group(1)
        candidates = ns_leaf.get(head)
        if not candidates:
            continue

        # `using N;` imports the TYPES inside N. It does NOT make N's own name usable as a
        # qualification prefix - `using A.B;` still does not license `B.Thing`. The head of a
        # dotted name binds only when the namespace is REACHABLE: it is top-level, or its
        # parent is this file's namespace or an enclosing one.
        ok = any((full == head) or (full.rsplit(".", 1)[-2] in chain) for full in candidates)
        if not ok:
            problems.append(
                f"{key}: writes '{head}.' - the namespace '{head}' is not reachable from "
                f"{ns}. A `using` will not license it; use the full dotted name, or import "
                f"the namespace and reference the type unqualified - CS0246")


# --- 6d. generic type arguments from a sibling namespace -------------------
# Check 6b only fires on a token followed by an identifier, so it walks
# `MatchPreset preset` but never `GetComponent<PlayerTwoDummy>()`. A generic argument
# occupies a type slot too, and a missing using there is the same CS0246 - and the
# one that bit here, since the editor tools reach across into ...Managers almost
# exclusively through GetComponent<>() and AddComponent<>().
for key, text in sources.items():
    ns = file_ns[key]
    if not ns:
        continue

    code = strip_code(text)
    for token in set(re.findall(r"<([A-Z]\w*)\s*[>,]", code)):
        if token in file_decls[key] or token in ENGINE_TYPES:
            continue

        for other_ns, names in declared_by_ns.items():
            if not other_ns or other_ns == ns:
                continue
            if ns.startswith(other_ns + ".") or other_ns.startswith(ns + "."):
                continue

            if token in names and other_ns not in file_usings[key]:
                problems.append(
                    f"{key}: uses '{token}' from {other_ns} but has no "
                    f"`using {other_ns};` - CS0246")


# --- 7. leftovers ----------------------------------------------------------
for key, src in sources.items():
    for marker in ("TODO", "FIXME", "NotImplementedException", "throw new System.Exception"):
        if marker in src:
            problems.append(f"{key}: contains '{marker}'")


# --- report ---------------------------------------------------------------
print("=" * 68)
print("MANUEL COMBAT SCRIPT CHECK")
print("=" * 68)

for note in notes:
    print(f"  ok    {note}")

if problems:
    print()
    print("-" * 68)
    for p in problems:
        print(f"  FAIL  {p}")
    print("-" * 68)
    print(f"\n{len(problems)} problem(s) found.")
    sys.exit(1)

print()
print(f"All checks passed ({len(notes)} assertions).")
