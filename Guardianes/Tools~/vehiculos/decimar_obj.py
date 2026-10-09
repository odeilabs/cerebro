"""Reduce mallas OBJ densas (las del marching cubes) con el Decimate de Blender.

    blender -b --factory-startup -P Tools/kong/decimar_obj.py -- <proporcion> <obj> [<proporcion> <obj> ...]

Sobrescribe cada OBJ con la proporcion de triangulos indicada. Los ejes se conservan (Blender
importa Y arriba -> Z arriba y exporta al reves); no escribe materiales, normales ni UV.
"""
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
for proporcion, ruta in zip(argv[0::2], argv[1::2]):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=ruta)
    o = bpy.context.selected_objects[0]
    antes = len(o.data.polygons)
    m = o.modifiers.new("Decimar", "DECIMATE")
    m.ratio = float(proporcion)
    m.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)
    despues = len(o.data.polygons)
    bpy.ops.wm.obj_export(filepath=ruta, export_selected_objects=True, export_materials=False,
                          export_normals=False, export_uv=False)
    print(f"[GD] {ruta}: {antes} -> {despues} triangulos")
