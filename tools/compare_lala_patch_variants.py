"""Compare Unit TOC/GPU structure across supplied Lala patch variants."""

import argparse
import hashlib
import json
import struct
from collections import Counter
from pathlib import Path

UNIT_TYPE_ID = 0xE0A48D0BE9A7453F
MATERIAL_TYPE_ID = 0xEAC0B497876ADEDF
MAGIC = 4026531857
TYPE_SIZE = 32
ENTRY_SIZE = 80


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def u64(data: bytes, offset: int) -> int:
    return struct.unpack_from("<Q", data, offset)[0]


def resolve_entries_offset(data: bytes, type_count: int, file_count: int) -> int:
    def score(type_offset: int) -> int:
        entries_offset = type_offset + type_count * TYPE_SIZE
        if entries_offset + file_count * ENTRY_SIZE > len(data):
            return -10**9
        types = set()
        declared_count = 0
        for index in range(type_count):
            offset = type_offset + index * TYPE_SIZE
            types.add(u64(data, offset + 8))
            declared_count += u64(data, offset + 16)
        value = 1000 if declared_count == file_count else 0
        for index in range(file_count):
            offset = entries_offset + index * ENTRY_SIZE
            if u64(data, offset):
                value += 1
            if u64(data, offset + 8) in types:
                value += 10
        return value

    return 72 + type_count * TYPE_SIZE if score(72) > score(60) else 60 + type_count * TYPE_SIZE


def read_patch(path: Path) -> list[dict]:
    data = path.read_bytes()
    magic, type_count, file_count = struct.unpack_from("<III", data)
    if magic != MAGIC:
        raise ValueError(f"not a patch TOC: {path}")
    entries_offset = resolve_entries_offset(data, type_count, file_count)
    entries = []
    for index in range(file_count):
        offset = entries_offset + index * ENTRY_SIZE
        fields = struct.unpack_from("<QQQQQQQIIIIII", data, offset)
        file_id, type_id, toc_offset, stream_offset, gpu_offset, unknown1, unknown2, toc_size, stream_size, gpu_size, unknown3, unknown4, unknown5 = fields
        entries.append({
            "file_id": file_id, "type_id": type_id, "toc_offset": toc_offset,
            "stream_offset": stream_offset, "gpu_offset": gpu_offset,
            "toc_size": toc_size, "stream_size": stream_size, "gpu_size": gpu_size,
            "unknown": [unknown1, unknown2, unknown3, unknown4, unknown5],
        })
    return entries


def payload(path: Path, entry: dict, suffix: str, offset_name: str, size_name: str) -> bytes:
    size = entry[size_name]
    if not size:
        return b""
    return (path if not suffix else Path(str(path) + suffix)).read_bytes()[entry[offset_name]:entry[offset_name] + size]


def stream_summary(toc: bytes) -> list[dict]:
    if len(toc) < 0x70:
        return []
    stream_info_offset = u32(toc, 0x5C)
    if not stream_info_offset or stream_info_offset + 4 > len(toc):
        return []
    count = u32(toc, stream_info_offset)
    streams = []
    for index in range(count):
        record_offset = stream_info_offset + 4 + index * 4
        if record_offset + 4 > len(toc):
            break
        relative = u32(toc, record_offset)
        offset = stream_info_offset + relative
        if offset + 368 > len(toc):
            streams.append({"index": index, "invalid_offset": offset})
            continue
        component_id = u64(toc, offset)
        component_count = u64(toc, offset + 328)
        stride = u32(toc, offset + 356)
        components = []
        for component_index in range(min(component_count, 16)):
            component_offset = offset + 8 + component_index * 20
            components.append({
                "type": u32(toc, component_offset),
                "format": u32(toc, component_offset + 4),
                "index": u32(toc, component_offset + 8),
                "unknown": f"0x{u64(toc, component_offset + 12):016x}",
            })
        streams.append({
            "index": index, "offset": offset, "component_info_id": f"0x{component_id:016x}",
            "component_count": component_count, "vertex_stride": stride, "components": components,
        })
    return streams


def mesh_summary(toc: bytes, gpu: bytes) -> tuple[list[dict], list[dict]]:
    if len(toc) < 0x74:
        return [], []
    mesh_info_offset = u32(toc, 0x64)
    materials_offset = u32(toc, 0x70)
    materials = []
    if materials_offset and materials_offset + 4 <= len(toc):
        count = u32(toc, materials_offset)
        slot_offset = materials_offset + 4
        material_id_offset = slot_offset + count * 4
        for index in range(count):
            if material_id_offset + (index + 1) * 8 > len(toc):
                break
            materials.append({"slot": u32(toc, slot_offset + index * 4), "material_id": f"0x{u64(toc, material_id_offset + index * 8):016x}"})
    if not mesh_info_offset or mesh_info_offset + 4 > len(toc):
        return [], materials
    meshes = []
    for index in range(u32(toc, mesh_info_offset)):
        table_offset = mesh_info_offset + 4 + index * 4
        if table_offset + 4 > len(toc):
            break
        offset = mesh_info_offset + u32(toc, table_offset)
        if offset + 128 > len(toc):
            continue
        material_count, material_relative = u32(toc, offset + 104), u32(toc, offset + 108)
        section_count, section_relative = u32(toc, offset + 120), u32(toc, offset + 124)
        slots = [u32(toc, offset + material_relative + item * 4) for item in range(material_count) if offset + material_relative + item * 4 + 4 <= len(toc)]
        sections = []
        for section_index in range(section_count):
            section_offset = offset + section_relative + section_index * 24
            if section_offset + 24 > len(toc):
                break
            material_index, vertex_offset, vertex_count, index_offset, index_count, group_index = struct.unpack_from("<IIIIII", toc, section_offset)
            sections.append({"material_index": material_index, "slot": slots[material_index] if material_index < len(slots) else None, "vertex_count": vertex_count, "index_count": index_count, "group_index": group_index})
        meshes.append({"index": index, "stream_index": u32(toc, offset + 64), "transform_index": u32(toc, offset + 72), "lod_index": u32(toc, offset + 80), "slots": slots, "sections": sections})
    return meshes, materials


def summarize(label: str, path: Path) -> dict:
    entries = read_patch(path)
    units = [entry for entry in entries if entry["type_id"] == UNIT_TYPE_ID]
    materials = [entry for entry in entries if entry["type_id"] == MATERIAL_TYPE_ID]
    textures = [entry for entry in entries if entry["type_id"] == 0xCD4238C6A0C69E32]
    result = {
        "label": label,
        "path": str(path),
        "entry_count": len(entries),
        "type_counts": {f"0x{k:016x}": v for k, v in Counter(entry["type_id"] for entry in entries).items()},
        "unit_count": len(units),
        "material_ids": [f"0x{entry['file_id']:016x}" for entry in materials],
        "material_payloads": [{"id": f"0x{entry['file_id']:016x}", "toc_sha256": hashlib.sha256(payload(path, entry, "", "toc_offset", "toc_size")).hexdigest()} for entry in materials],
        "texture_payloads": [{"id": f"0x{entry['file_id']:016x}", "toc_sha256": hashlib.sha256(payload(path, entry, "", "toc_offset", "toc_size")).hexdigest(), "gpu_sha256": hashlib.sha256(payload(path, entry, ".gpu_resources", "gpu_offset", "gpu_size")).hexdigest()} for entry in textures],
        "units": [],
    }
    for entry in units:
        toc = payload(path, entry, "", "toc_offset", "toc_size")
        gpu = payload(path, entry, ".gpu_resources", "gpu_offset", "gpu_size")
        meshes, bindings = mesh_summary(toc, gpu)
        result["units"].append({
            "id": f"0x{entry['file_id']:016x}",
            "toc_size": len(toc), "gpu_size": len(gpu),
            "toc_sha256": hashlib.sha256(toc).hexdigest(),
            "gpu_sha256": hashlib.sha256(gpu).hexdigest(),
            "stream_info_offset": f"0x{u32(toc, 0x5c):x}" if len(toc) >= 0x60 else None,
            "mesh_info_offset": f"0x{u32(toc, 0x60):x}" if len(toc) >= 0x64 else None,
            "materials_offset": f"0x{u32(toc, 0x70):x}" if len(toc) >= 0x74 else None,
            "streams": stream_summary(toc),
            "material_bindings": bindings,
            "meshes": meshes,
        })
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("items", nargs="+", metavar="LABEL=PATCH")
    args = parser.parse_args()
    summaries = []
    for item in args.items:
        label, value = item.split("=", 1)
        summaries.append(summarize(label, Path(value)))
    args.output.write_text(json.dumps({"patches": summaries}, indent=2), encoding="utf-8")
    print(args.output)
    for summary in summaries:
        print(f"{summary['label']}: entries={summary['entry_count']} units={summary['unit_count']} materials={len(summary['material_ids'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
