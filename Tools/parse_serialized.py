"""
Parse a Unity SerializedFile (Library/Artifacts/...) and list its objects:
pathID + classID. This lets us recover the fileIDs Unity assigns to objects
inside an imported .fbx, which we need when hand-authoring scene YAML that
references model sub-assets (GameObject / Transform / AnimationClip).

Format reference (Unity serialized file, version 15..22):
  0x00 int32 metadataSize
  0x04 int32 fileSize
  0x08 int32 version
  0x0C int32 dataOffset
  0x10 uint8 endianness (0 = little)
  0x11 3 bytes reserved
  0x14 int32 metadataSize (version >= 22, "extended")
  0x18 unityVersion string (null terminated, 4-byte aligned)  [version >= 7]
       int32 targetPlatform                                    [version >= 8]
       uint8 enableTypeTree                                    [version >= 13]
  metadata:
       int32 typeCount
       [type tree blobs]
       int32 objectCount
       object entries: int64 pathID, int32 byteStart, int32 byteSize, int32 typeID
"""
import struct
import sys

CLASS_NAMES = {
    1: "GameObject",
    4: "Transform",
    21: "Material",
    23: "MeshRenderer",
    33: "MeshFilter",
    43: "Mesh",
    74: "AnimationClip",
    90: "Avatar",
    91: "AnimatorController",
    95: "Animator",
    1102: "AnimatorState",
    1101: "AnimatorStateTransition",
    1107: "AnimatorStateMachine",
    114: "MonoBehaviour",
    137: "SkinnedMeshRenderer",
    1001: "PrefabInstance",
    1660057539: "SceneRoots",
}


def read_cstring(buf, off):
    end = buf.index(b"\x00", off)
    s = buf[off:end].decode("utf-8", "replace")
    off = end + 1
    # 4-byte align
    off = (off + 3) & ~3
    return s, off


def parse(path):
    buf = open(path, "rb").read()
    off = 0
    metadata_size, file_size, version, data_offset = struct.unpack_from("<iiii", buf, 0)
    endianness = buf[0x10]
    off = 0x14
    if version >= 22:
        off += 4  # extended metadata size
    unity_version, off = read_cstring(buf, off)
    target_platform = None
    if version >= 8:
        target_platform, = struct.unpack_from("<i", buf, off)
        off += 4
    enable_type_tree = 0
    if version >= 13:
        enable_type_tree = buf[off]
        off += 1

    print(f"version={version} metadataSize={metadata_size} dataOffset={data_offset} "
          f"endianness={endianness} unity={unity_version} platform={target_platform} "
          f"typeTree={enable_type_tree}")

    meta_start = off
    type_count, = struct.unpack_from("<i", buf, meta_start)
    off = meta_start + 4
    print("typeCount =", type_count)

    for _ in range(type_count):
        class_id, = struct.unpack_from("<i", buf, off)
        off += 4
        if version >= 16:
            is_stripped = buf[off]
            off += 1
        script_type_index = None
        if version >= 17:
            script_type_index, = struct.unpack_from("<h", buf, off)
            off += 2
        if version >= 13:
            if version >= 16:
                off += 1  # hasTypeTree (unused here)
            # node count + string buffer size
            node_count, = struct.unpack_from("<i", buf, off)
            off += 4
            str_buf_size, = struct.unpack_from("<i", buf, off)
            off += 4
            # skip the type tree blob (best effort): each node is 32 bytes + string
            off += node_count * 32
            off += str_buf_size
            if version >= 21:
                dep_count, = struct.unpack_from("<i", buf, off)
                off += 4
                off += dep_count * 4

    object_count, = struct.unpack_from("<i", buf, off)
    off += 4
    print("objectCount =", object_count)

    objects = []
    for _ in range(object_count):
        if version >= 14:
            off = (off + 3) & ~3
        path_id, = struct.unpack_from("<q", buf, off)
        off += 8
        byte_start, = struct.unpack_from("<i", buf, off)
        off += 4
        byte_size, = struct.unpack_from("<i", buf, off)
        off += 4
        type_id, = struct.unpack_from("<i", buf, off)
        off += 4
        objects.append((path_id, byte_start, byte_size, type_id))

    return objects


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else \
        r"C:\Dev\projectLEA\Library\Artifacts\77\77d5f0fd248045da47c5b8b628221e85"
    objs = parse(path)
    print("\npathID            classID  name")
    for pid, bs, size, tid in objs:
        print(f"{pid:<18}{tid:<9}{CLASS_NAMES.get(tid, '?')}  (size={size})")
