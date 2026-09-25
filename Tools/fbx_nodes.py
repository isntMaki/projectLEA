"""Minimal binary-FBX reader: prints the object hierarchy with the transforms Unity will see.

Blender's FBX export applies its axis conversion to the ROOT node, not to the vertex
data, so a vertex dump alone says nothing about the model's orientation in Unity. This
walks the node tree and reports every Model node's name, parent and Lcl transform,
plus the animation stacks.

The file has no single wrapping root: Definitions/Objects/Connections/Takes are
sibling top-level nodes, so they are all read.
"""

import struct
import sys

TYPE_CODES = ('Y', 'C', 'I', 'F', 'D', 'L', 'T', 'R', 'S', 'd', 'f', 'i', 'l', 'b')


class Reader:
    def __init__(self, path):
        self.buf = open(path, 'rb').read()
        assert self.buf[:18] == b'Kaydara FBX Binary', "not a binary FBX"
        self.version = struct.unpack('<I', self.buf[23:27])[0]
        self.big = self.version >= 7500
        self.pos = 27

    def u32(self):
        v = struct.unpack('<I', self.buf[self.pos:self.pos + 4])[0]
        self.pos += 4
        return v

    def u64(self):
        v = struct.unpack('<Q', self.buf[self.pos:self.pos + 8])[0]
        self.pos += 8
        return v

    def count(self):
        return self.u64() if self.big else self.u32()

    def read_nodes(self):
        """Top-level nodes run to the null record at the end of the file."""
        out = []
        while self.pos < len(self.buf) - 13:
            node = self.node()
            if node is None:
                break
            out.append(node)
        return out

    def node(self):
        start = self.pos
        end = self.count()
        if end == 0:
            return None
        nprops = self.count()
        plist = self.count()
        namelen = self.buf[self.pos]
        self.pos += 1
        name = self.buf[self.pos:self.pos + namelen].decode('ascii', 'replace')
        self.pos += namelen
        props = [self.property() for _ in range(nprops)]
        children = []
        while self.pos < end:
            child = self.node()
            if child is None:
                break
            children.append(child)
        self.pos = end
        return (name, props, children)

    def property(self):
        # Blender's binary writer emits properties WITHOUT names: a one-byte type code
        # directly followed by the data. Verified against the recorded property-list
        # length (an 'I' + int32 is exactly 5 bytes; a P record made of two 15-char
        # strings, an empty string, a 1-char flag and three doubles is exactly 78).
        code = self.buf[self.pos:self.pos + 1].decode('ascii', 'replace')
        self.pos += 1
        if code == 'Y':
            v = struct.unpack('<h', self.buf[self.pos:self.pos + 2])[0]
            self.pos += 2
            return (code, v)
        if code == 'C':
            v = self.buf[self.pos]
            self.pos += 1
            return (code, v)
        if code == 'I':
            v = struct.unpack('<i', self.buf[self.pos:self.pos + 4])[0]
            self.pos += 4
            return (code, v)
        if code in ('L', 'T'):
            v = struct.unpack('<q', self.buf[self.pos:self.pos + 8])[0]
            self.pos += 8
            return (code, v)
        if code == 'F':
            v = struct.unpack('<f', self.buf[self.pos:self.pos + 4])[0]
            self.pos += 4
            return (code, v)
        if code == 'D':
            v = struct.unpack('<d', self.buf[self.pos:self.pos + 8])[0]
            self.pos += 8
            return (code, v)
        if code in ('R', 'S'):
            ln = self.u32()
            raw = self.buf[self.pos:self.pos + ln]
            self.pos += ln
            if code == 'S':
                return (code, raw.decode('utf-8', 'replace'))
            return (code, raw)
        if code in TYPE_CODES[-5:]:
            ln = self.count()
            enc = self.u32()
            comp = self.u32()
            raw = self.buf[self.pos:self.pos + comp]
            self.pos += comp
            if enc == 1:
                import zlib
                raw = zlib.decompress(raw)
            # FBX 'l' arrays are 64-bit; Python struct 'l' is 32-bit on Windows, so use 'q'.
            fmt = {'d': '<%dd', 'f': '<%df', 'i': '<%di', 'l': '<%dq', 'b': '<%dB'}[code] % ln
            return (code, list(struct.unpack(fmt, raw)))
        raise ValueError("unknown property code %r at %d" % (code, self.pos))


def p_record(node):
    """A Properties70 child is a node named 'P' whose properties are the field list:
    name, type, subtype, flags, then the values. Returns (name, [values])."""
    _, props, _ = node
    if not props:
        return None
    name = props[0][1] if isinstance(props[0][1], str) else None
    if name is None:
        return None
    values = []
    for code, v in props[4:]:
        values.append(v)
    return (name, values)


def transforms_of(model_node):
    """Pulls Lcl Translation / Rotation / Scaling / PreRotation out of Properties70."""
    out = {}
    for child in model_node[2]:
        if child[0] != 'Properties70':
            continue
        for p in child[2]:
            if p[0] != 'P':
                continue
            rec = p_record(p)
            if rec is None:
                continue
            nm, vals = rec
            if nm in ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling', 'PreRotation'):
                out[nm] = [round(float(x), 6) for x in vals[-3:]]
    return out


def main(path):
    r = Reader(path)
    tops = r.read_nodes()

    objects = next((n for n in tops if n[0] == 'Objects'), None)
    connections = next((n for n in tops if n[0] == 'Connections'), None)
    takes = next((n for n in tops if n[0] == 'Takes'), None)

    print("=== %s (FBX %d, top-level: %s) ===" %
          (path.split('\\')[-1], r.version, [n[0] for n in tops]))

    if objects is None:
        print("no Objects node")
        return

    # Model id -> (name, transform); connections give the parenting.
    models = {}
    deformers = {}
    for child in objects[2]:
        if child[0] == 'Model' and child[1]:
            uid = None
            nm = child[1][0][1] if isinstance(child[1][0][1], str) else None
            # The unique id is the first property of the Model node in ASCII files; in
            # this binary layout the id is not stored on the node, so key by name.
            models[nm] = transforms_of(child)
        if child[0] == 'Deformer' and child[1]:
            nm = child[1][0][1] if isinstance(child[1][0][1], str) else None
            deformers[nm] = True

    # Parenting: 'OO' connection records are (code, child, parent).
    parents = {}
    if connections is not None:
        for c in connections[2]:
            if c[0] != 'C':
                continue
            vals = [p[1] for p in c[1]]
            if vals and vals[0] == 'OO' and len(vals) >= 3:
                parents[vals[1]] = vals[2]

    for nm, tr in models.items():
        print("MODEL %-28s %s" % (nm, tr))

    if takes is not None:
        for c in takes[2]:
            if c[0] == 'Take':
                print("TAKE  %s" % c[1][0][1] if c[1] else c[0])

    print("models: %d, deformers: %d, connections: %d" %
          (len(models), len(deformers), len(parents)))


for p in sys.argv[1:]:
    main(p)
    print()
