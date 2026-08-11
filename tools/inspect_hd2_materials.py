import bpy
import os
from collections import Counter

focus = {
    "0x3215fd46bb3be910",
    "0xaad1dc7643acff2a",
    "0x602c4a614d595850",
    "0xc57a0c00b4c61712",
    "0xdd0d469d9bc4d9c2",
    "0xa70972419680b0b2",
}
if os.environ.get("HD2_REPORT_ALL") == "1":
    focus = set()

output_path = os.environ.get("HD2_MATERIAL_REPORT", "hd2-material-report.txt")
rows = []
for obj in sorted(bpy.data.objects, key=lambda item: item.name):
    if obj.type != "MESH":
        continue
    unit_id = str(obj.get("Z_ObjectID", ""))
    try:
        unit_hex = f"0x{int(unit_id):016x}" if unit_id else ""
    except ValueError:
        unit_hex = unit_id.lower()
    if focus and unit_hex not in focus and unit_id.lower() not in focus:
        continue
    mesh_index = obj.get("MeshInfoIndex", "?")
    material_names = [slot.material.name if slot.material else "<empty>" for slot in obj.material_slots]
    counts = Counter(poly.material_index for poly in obj.data.polygons)
    usage = "; ".join(
        f"slot={index},name={material_names[index] if index < len(material_names) else '<out-of-range>'},polygons={counts.get(index, 0)}"
        for index in range(max(len(material_names), max(counts.keys(), default=-1) + 1))
    )
    props = ",".join(f"{key}={obj.get(key)}" for key in obj.keys())
    rows.append(
        f"object={obj.name}; unit={unit_hex}; meshInfo={mesh_index}; props={props}; "
        f"culling={obj.display_type == 'WIRE'}; {usage}"
    )

with open(output_path, "w", encoding="utf-8") as report:
    report.write("\n".join(rows))
print(f"HD2 material report: {output_path} ({len(rows)} objects)")
