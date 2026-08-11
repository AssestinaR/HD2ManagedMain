import bpy
import os
import sys

def find_sdk():
    # Blender exposes many operator objects with similarly named attributes.
    # Require the actual SDK module and its TocManager instance.
    for name in ("HD2SDK-CommunityEdition", "HD2SDK_CommunityEdition"):
        module = sys.modules.get(name)
        manager = getattr(module, "Global_TocManager", None) if module else None
        if module is not None and hasattr(module, "TocManager") and isinstance(manager, module.TocManager):
            return module
    for name, module in list(sys.modules.items()):
        if module is None or not hasattr(module, "TocManager"):
            continue
        manager = getattr(module, "Global_TocManager", None)
        if isinstance(manager, module.TocManager) and hasattr(module, "UnitID"):
            print(f"Using SDK module: {name}")
            return module
    raise RuntimeError("HD2SDK Community Edition is not loaded in Blender")

sdk = find_sdk()
# Allow the SDK to build its normal game-data search index. Unit armatures and
# state machines are usually outside the output patch and are needed by the
# Blender importer even when materials are represented by placeholders.
sdk.Global_TocManager.SearchArchives = []
settings = bpy.context.scene.Hd2ToolPanelSettings
settings.ImportMaterials = False
settings.ImportLods = True
settings.ImportCulling = True
settings.ImportStatic = False
settings.MakeCollections = True
settings.Force3UVs = True
settings.Force1Group = False
settings.ImportArmature = True
settings.MergeArmatures = False

material_patch = os.environ["HD2_MATERIAL_PATCH"]
unit_patch = os.environ["HD2_UNIT_PATCH"]
unit_ids = [int(value, 16) for value in os.environ["HD2_UNIT_IDS"].split(",") if value]
blend_path = os.environ["HD2_BLEND_OUTPUT"]

# Load the provider first, then make the tested Unit patch active.
# Register the material patch without activating it.  SDK 3.8.0 eagerly
# creates Blender node trees for active material patches, which is unrelated
# to this slot/index inspection and fails with some Blender 4.3 templates.
sdk.Global_TocManager.LoadArchive(material_patch, False, True)
sdk.Global_TocManager.LoadArchive(unit_patch, True, True)

for unit_id in unit_ids:
    entry = sdk.Global_TocManager.GetEntry(unit_id, sdk.UnitID, SearchAll=True, IgnorePatch=False)
    if entry is None:
        print(f"Missing Unit 0x{unit_id:016x}")
        continue
    print(f"Loading Unit 0x{unit_id:016x}")
    entry.Load(False, True, True)

bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print(f"Saved Blender inspection file: {blend_path}")
