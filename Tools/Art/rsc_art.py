"""Builds the RSC block and disk models in Blender and renders their icons.

Run headless from the repo root (Blender 4.2+ / 5.x):

    blender -b --factory-startup -P Tools/Art/rsc_art.py -- <repo root> [--lineup-only]

Outputs:
    Mod/UIAtlases/ItemIconAtlas/<name>.png   160x160 inventory icons (shipped)
    Art/Renders/<name>.png                   512x512 previews
    Art/Renders/lineup.png                   every model side by side
    Art/Models/<name>.fbx                    meshes for a later Unity bundle
    Art/rsc_models.blend                     the whole scene

Every model is built from primitives here, so changing a colour or a part
and re-running the script regenerates everything. Blocks are 1 x 1 x 1 m,
centred on x/y, standing on z = 0, front facing -Y (the game's convention
for a block's front once imported into Unity is set up there).
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = os.path.abspath(ARGS[0]) if ARGS else os.getcwd()
LINEUP_ONLY = "--lineup-only" in ARGS

ICON_DIR = os.path.join(ROOT, "Mod", "UIAtlases", "ItemIconAtlas")
RENDER_DIR = os.path.join(ROOT, "Art", "Renders")
MODEL_DIR = os.path.join(ROOT, "Art", "Models")
BLEND_PATH = os.path.join(ROOT, "Art", "rsc_models.blend")


# ---------------------------------------------------------------- helpers

def srgb(hex_color):
    """'4A90E2' -> linear RGB tuple."""
    out = []
    for i in (0, 2, 4):
        c = int(hex_color[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return tuple(out)


MATERIALS = {}


def material(name, color, metallic=0.7, roughness=0.38, emission=None, strength=0.0):
    if name in MATERIALS:
        return MATERIALS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    MATERIALS[name] = m
    return m


def body_mat():
    return material("rsc_body", srgb("23272E"), metallic=0.75, roughness=0.42)


def panel_mat():
    return material("rsc_panel", srgb("3A4049"), metallic=0.7, roughness=0.5)


def trim_mat():
    return material("rsc_trim", srgb("9AA3AD"), metallic=0.95, roughness=0.25)


def recess_mat():
    return material("rsc_recess", srgb("0C0E11"), metallic=0.3, roughness=0.6)


def glow_mat(hex_color, strength=2.5):
    c = srgb(hex_color)
    return material("rsc_glow_%s_%g" % (hex_color, strength), c, metallic=0.0, roughness=0.3, emission=c, strength=strength)


def accent_mat(hex_color):
    return material("rsc_accent_" + hex_color, srgb(hex_color), metallic=0.4, roughness=0.35)


def link(obj, parent):
    obj.parent = parent
    return obj


def bevel(obj, width, segments=3):
    if width > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = width
        mod.segments = segments
        mod.limit_method = "ANGLE"
    return obj


def box(parent, name, size, loc, mat, bevel_width=0.015, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel(o, bevel_width)
    o.data.materials.append(mat)
    for poly in o.data.polygons:
        poly.use_smooth = True
    return link(o, parent)


def cylinder(parent, name, radius, depth, loc, mat, rot=(0, 0, 0), verts=40, bevel_width=0.006):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    bevel(o, bevel_width, 2)
    o.data.materials.append(mat)
    return link(o, parent)


def torus(parent, name, major, minor, loc, mat, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, location=loc, rotation=rot,
                                     major_segments=48, minor_segments=12)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    return link(o, parent)


def text(parent, name, body, size, loc, mat, rot=(math.radians(90), 0, 0), extrude=0.004):
    bpy.ops.object.text_add(location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.data.body = body
    o.data.size = size
    o.data.extrude = extrude
    o.data.align_x = "CENTER"
    o.data.align_y = "CENTER"
    o.data.materials.append(mat)
    return link(o, parent)


def root(name):
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=(0, 0, 0))
    e = bpy.context.active_object
    e.name = name
    return e


def frame_shell(parent, prefix, face, center, width, height, thickness, mat):
    """Four bars forming a rectangular frame on the -Y (front) or +X (side) face."""
    cx, cy, cz = center
    half_w, half_h = width / 2, height / 2
    if face == "front":
        box(parent, prefix + "_t", (width, thickness, thickness), (cx, cy, cz + half_h), mat, 0.004)
        box(parent, prefix + "_b", (width, thickness, thickness), (cx, cy, cz - half_h), mat, 0.004)
        box(parent, prefix + "_l", (thickness, thickness, height), (cx - half_w, cy, cz), mat, 0.004)
        box(parent, prefix + "_r", (thickness, thickness, height), (cx + half_w, cy, cz), mat, 0.004)
    else:
        box(parent, prefix + "_t", (thickness, width, thickness), (cx, cy, cz + half_h), mat, 0.004)
        box(parent, prefix + "_b", (thickness, width, thickness), (cx, cy, cz - half_h), mat, 0.004)
        box(parent, prefix + "_l", (thickness, thickness, height), (cx, cy - half_w, cz), mat, 0.004)
        box(parent, prefix + "_r", (thickness, thickness, height), (cx, cy + half_w, cz), mat, 0.004)


def chassis(parent, prefix, height=0.92):
    """Shared body: a bevelled dark case with steel corner posts and a top cap."""
    box(parent, prefix + "_case", (0.9, 0.9, height), (0, 0, height / 2), body_mat(), 0.03)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(parent, prefix + "_post", (0.08, 0.08, height + 0.02), (sx * 0.44, sy * 0.44, height / 2 + 0.01),
                trim_mat(), 0.012)
    box(parent, prefix + "_cap", (0.82, 0.82, 0.04), (0, 0, height + 0.01), panel_mat(), 0.01)


def side_vents(parent, prefix, count=5, z0=0.25, step=0.1, accent=None):
    for i in range(count):
        z = z0 + i * step
        box(parent, prefix + "_vent%d" % i, (0.02, 0.56, 0.035), (0.455, 0, z), recess_mat(), 0.004)
    if accent:
        box(parent, prefix + "_sidestrip", (0.012, 0.6, 0.03), (0.462, 0, z0 - 0.08), glow_mat(accent, 2.5), 0.003)


# ---------------------------------------------------------------- models

def controller(name, accent, advanced):
    r = root(name)
    chassis(r, name)
    # Front panel with the glowing core.
    box(r, name + "_panel", (0.7, 0.04, 0.72), (0, -0.455, 0.47), panel_mat(), 0.01)
    torus(r, name + "_ring", 0.22, 0.035, (0, -0.48, 0.52), trim_mat(), rot=(math.radians(90), 0, 0))
    cylinder(r, name + "_core", 0.19, 0.04, (0, -0.48, 0.52), glow_mat(accent, 3.0 if advanced else 2.2),
             rot=(math.radians(90), 0, 0))
    cylinder(r, name + "_coreeye", 0.07, 0.05, (0, -0.495, 0.52), recess_mat(), rot=(math.radians(90), 0, 0))
    # Status lights under the core.
    for i in range(3 if advanced else 2):
        x = (i - (1 if advanced else 0.5)) * 0.12
        box(r, name + "_led%d" % i, (0.07, 0.02, 0.025), (x, -0.48, 0.2), glow_mat(accent, 2.5), 0.004)
    side_vents(r, name, accent=accent)
    if advanced:
        # Power coil on top and cooling fins on the side.
        cylinder(r, name + "_coil", 0.16, 0.12, (0, 0, 1.0), trim_mat())
        for i in range(3):
            torus(r, name + "_band%d" % i, 0.165, 0.012, (0, 0, 0.96 + i * 0.04), glow_mat(accent, 3.0))
        for i in range(4):
            box(r, name + "_fin%d" % i, (0.06, 0.02, 0.6), (0.48, -0.24 + i * 0.16, 0.5), trim_mat(), 0.006)
    return r


def relay(name, accent):
    r = root(name)
    box(r, name + "_base", (0.6, 0.6, 0.06), (0, 0, 0.03), panel_mat(), 0.012)
    box(r, name + "_hub", (0.42, 0.42, 0.42), (0, 0, 0.5), body_mat(), 0.035)
    # Connectors on every face but the base, with glowing collars.
    dirs = [((1, 0, 0), (0, math.radians(90), 0)), ((-1, 0, 0), (0, math.radians(90), 0)),
            ((0, 1, 0), (math.radians(90), 0, 0)), ((0, -1, 0), (math.radians(90), 0, 0)),
            ((0, 0, 1), (0, 0, 0))]
    for i, (d, rot) in enumerate(dirs):
        c = Vector((0, 0, 0.5)) + Vector(d) * 0.3
        cylinder(r, name + "_plug%d" % i, 0.075, 0.2, tuple(c), trim_mat(), rot=rot)
        collar = Vector((0, 0, 0.5)) + Vector(d) * 0.24
        cylinder(r, name + "_collar%d" % i, 0.095, 0.03, tuple(collar), glow_mat(accent, 2.5), rot=rot)
    box(r, name + "_stem", (0.14, 0.14, 0.26), (0, 0, 0.18), trim_mat(), 0.01)
    return r


def interface(name, accent):
    r = root(name)
    chassis(r, name)
    for face, center in (("front", (0, -0.465, 0.47)), ("side", (0.465, 0, 0.47))):
        frame_shell(r, name + "_" + face + "frame", face, center, 0.64, 0.64, 0.045, glow_mat(accent, 2.0))
        # A 3x3 grid of sockets.
        for i in range(3):
            for j in range(3):
                off_a = (i - 1) * 0.18
                off_z = (j - 1) * 0.18
                if face == "front":
                    box(r, "%s_sock%d%d_f" % (name, i, j), (0.13, 0.03, 0.13),
                        (off_a, -0.455, 0.47 + off_z), recess_mat(), 0.006)
                else:
                    box(r, "%s_sock%d%d_s" % (name, i, j), (0.03, 0.13, 0.13),
                        (0.455, off_a, 0.47 + off_z), recess_mat(), 0.006)
    box(r, name + "_topplate", (0.5, 0.5, 0.02), (0, 0, 0.962), accent_mat(accent), 0.006)  # sits on the cap (top 0.95)
    return r


def terminal(name, accent):
    r = root(name)
    # Pedestal.
    box(r, name + "_base", (0.9, 0.9, 0.5), (0, 0, 0.25), body_mat(), 0.03)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(r, name + "_post", (0.08, 0.08, 0.52), (sx * 0.44, sy * 0.44, 0.26), trim_mat(), 0.012)
    # Keyboard deck.
    box(r, name + "_deck", (0.76, 0.3, 0.04), (0, -0.25, 0.52), panel_mat(), 0.01,
        rot=(math.radians(-12), 0, 0))
    for i in range(4):
        box(r, name + "_keys%d" % i, (0.64, 0.035, 0.012), (0, -0.33 + i * 0.055, 0.535 + i * 0.012),
            recess_mat(), 0.003, rot=(math.radians(-12), 0, 0))
    # Tilted screen housing with a glowing item grid.
    tilt = math.radians(-18)
    housing_center = Vector((0, 0.12, 0.78))
    rot = Matrix.Rotation(tilt, 3, "X")
    box(r, name + "_housing", (0.84, 0.12, 0.54), tuple(housing_center), body_mat(), 0.025, rot=(tilt, 0, 0))
    screen_center = housing_center + rot @ Vector((0, -0.065, 0))
    box(r, name + "_screen", (0.72, 0.01, 0.44), tuple(screen_center), glow_mat(accent, 2.2), 0.0,
        rot=(tilt, 0, 0))
    for i in range(4):
        for j in range(3):
            local = Vector(((i - 1.5) * 0.16, -0.075, (j - 1) * 0.13))
            box(r, "%s_cell%d%d" % (name, i, j), (0.12, 0.008, 0.1), tuple(housing_center + rot @ local),
                material("rsc_screencell", srgb("0F2A12"), metallic=0.0, roughness=0.5,
                         emission=srgb("1E6B24"), strength=1.0), 0.0, rot=(tilt, 0, 0))
    return r


def drive_cabinet(name, accent):
    r = root(name)
    chassis(r, name)
    box(r, name + "_face", (0.76, 0.03, 0.8), (0, -0.452, 0.47), panel_mat(), 0.01)
    # Six drive bays, two columns of three.
    for col in range(2):
        for row in range(3):
            x = (col - 0.5) * 0.34
            z = 0.22 + row * 0.25
            box(r, "%s_bay%d%d" % (name, col, row), (0.3, 0.03, 0.2), (x, -0.462, z), recess_mat(), 0.006)
            box(r, "%s_drive%d%d" % (name, col, row), (0.26, 0.03, 0.16), (x, -0.47, z), body_mat(), 0.008)
            box(r, "%s_handle%d%d" % (name, col, row), (0.16, 0.02, 0.025), (x, -0.487, z - 0.04), trim_mat(), 0.004)
            box(r, "%s_led%d%d" % (name, col, row), (0.05, 0.02, 0.05), (x + 0.085, -0.487, z + 0.035),
                glow_mat(accent, 3.0), 0.004)
    side_vents(r, name, count=6, z0=0.2, step=0.1, accent=accent)
    box(r, name + "_topplate", (0.5, 0.5, 0.02), (0, 0, 0.962), accent_mat(accent), 0.006)  # sits on the cap (top 0.95)
    box(r, name + "_trim", (0.76, 0.035, 0.03), (0, -0.47, 0.88), glow_mat(accent, 2.5), 0.004)
    return r


DISK_TIERS = [
    ("rscDisk1k", "1K", "B0B0B0"),
    ("rscDisk10k", "10K", "60C060"),
    ("rscDisk100k", "100K", "5090E0"),
    ("rscDisk250k", "250K", "A060E0"),
    ("rscDisk500k", "500K", "E0A040"),
    ("rscDisk1m", "1M", "E05050"),
]


def disk(name, label, accent):
    """A drive cartridge standing upright: the label faces -Y."""
    r = root(name)
    box(r, name + "_shell", (0.62, 0.11, 0.44), (0, 0, 0.22), body_mat(), 0.02)
    box(r, name + "_label", (0.5, 0.02, 0.2), (0, -0.056, 0.27), accent_mat(accent), 0.006)
    text(r, name + "_text", label, 0.13, (0, -0.07, 0.27), material("rsc_labeltext", srgb("101214"), 0.0, 0.5))
    box(r, name + "_stripe", (0.5, 0.02, 0.025), (0, -0.056, 0.115), glow_mat(accent, 2.5), 0.004)
    box(r, name + "_grip", (0.12, 0.13, 0.05), (0.22, 0, 0.42), trim_mat(), 0.01)
    # Gold contacts along the bottom edge.
    for i in range(7):
        box(r, name + "_pin%d" % i, (0.035, 0.08, 0.02), (-0.18 + i * 0.06, 0, 0.012),
            material("rsc_gold", srgb("D6A53A"), 1.0, 0.25), 0.003)
    return r


BLOCKS = [
    ("rscControllerSimple", lambda n: controller(n, "4AB0E2", advanced=False)),
    ("rscControllerAdvanced", lambda n: controller(n, "3355FF", advanced=True)),
    ("rscHub", lambda n: relay(n, "40C0C0")),
    ("rscInterface", lambda n: interface(n, "E0A040")),
    ("rscTerminal", lambda n: terminal(n, "60D060")),
    ("rscDriveCabinet", lambda n: drive_cabinet(n, "C060C0")),
]


# ---------------------------------------------------------------- scene

def setup_scene(size):
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    scene.render.resolution_x = size[0]
    scene.render.resolution_y = size[1]
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    try:
        scene.eevee.taa_render_samples = 64
    except AttributeError:
        pass

    world = bpy.data.worlds.new("rsc_world")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.32, 0.34, 0.38, 1.0)
    bg.inputs["Strength"].default_value = 0.9
    scene.world = world

    def light(name, kind, loc, energy, size=2.0, color=(1, 1, 1)):
        data = bpy.data.lights.new(name, kind)
        data.energy = energy
        data.color = color
        if kind == "AREA":
            data.size = size
        obj = bpy.data.objects.new(name, data)
        obj.location = loc
        direction = Vector((0, 0, 0.45)) - Vector(loc)
        obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(obj)

    light("key", "AREA", (2.5, -3.0, 4.0), 900, 3.0, (1.0, 0.97, 0.92))
    light("fill", "AREA", (-3.5, -2.0, 2.0), 350, 4.0, (0.85, 0.9, 1.0))
    light("rim", "AREA", (1.0, 3.5, 3.0), 500, 2.5, (0.8, 0.88, 1.0))

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


# The vanilla icons look at blocks from the front-right and a little above.
VIEW_DIR = Vector((1.0, -1.25, 0.95)).normalized()


def hierarchy(obj):
    out = [obj]
    for c in obj.children:
        out.extend(hierarchy(c))
    return out


def world_points(objs):
    pts = []
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.type not in {"MESH", "FONT"}:
            continue
        ev = o.evaluated_get(depsgraph)
        for corner in ev.bound_box:
            pts.append(ev.matrix_world @ Vector(corner))
    return pts


def frame_camera(cam, objs, margin=1.1, aspect=1.0):
    pts = world_points(objs)
    center = sum(pts, Vector()) / len(pts)
    rot = VIEW_DIR.to_track_quat("Z", "Y")  # camera looks down -Z, so +Z points back at the viewer
    cam.rotation_euler = rot.to_euler()
    inv = rot.to_matrix().inverted()
    local = [inv @ (p - center) for p in pts]
    xs = [p.x for p in local]
    ys = [p.y for p in local]
    mid = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, 0))
    width = max(xs) - min(xs)
    height = max(ys) - min(ys)
    cam.data.ortho_scale = max(width, height * aspect) * margin
    cam.location = center + rot.to_matrix() @ mid + VIEW_DIR * 10
    cam.data.clip_end = 40


def render(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def show_only(visible_roots, all_roots):
    for r in all_roots:
        hide = r not in visible_roots
        for o in hierarchy(r):
            o.hide_render = hide


def export_fbx(r):
    os.makedirs(MODEL_DIR, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in hierarchy(r):
        o.select_set(True)
    bpy.context.view_layer.objects.active = r
    try:
        bpy.ops.export_scene.fbx(filepath=os.path.join(MODEL_DIR, r.name + ".fbx"), use_selection=True,
                                 apply_unit_scale=True, use_mesh_modifiers=True, object_types={"EMPTY", "MESH"},
                                 bake_space_transform=True, apply_scale_options="FBX_SCALE_ALL")
    except Exception as e:  # the exporter is an add-on; icons matter more than FBX
        print("[rsc_art] FBX export skipped for %s: %s" % (r.name, e))


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cam = setup_scene((160, 160))

    roots = []
    for name, build in BLOCKS:
        roots.append(build(name))
    for name, label, accent in DISK_TIERS:
        roots.append(disk(name, label, accent))

    # Fonts become meshes so they export and bound correctly.
    for r in roots:
        for o in hierarchy(r):
            if o.type == "FONT":
                bpy.ops.object.select_all(action="DESELECT")
                o.select_set(True)
                bpy.context.view_layer.objects.active = o
                bpy.ops.object.convert(target="MESH")

    if not LINEUP_ONLY:
        for r in roots:
            show_only([r], roots)
            frame_camera(cam, hierarchy(r), margin=1.06)
            bpy.context.scene.render.resolution_x = 160
            bpy.context.scene.render.resolution_y = 160
            render(os.path.join(ICON_DIR, r.name + ".png"))
            bpy.context.scene.render.resolution_x = 512
            bpy.context.scene.render.resolution_y = 512
            render(os.path.join(RENDER_DIR, r.name + ".png"))
            export_fbx(r)

    # Lineup: blocks in a row, disks in front, on a dark backdrop.
    for i, r in enumerate(roots[:len(BLOCKS)]):
        r.location = ((i - 2.5) * 1.35, 0, 0)
    for i, r in enumerate(roots[len(BLOCKS):]):
        r.location = ((i - 2.5) * 1.35, -1.6, 0)
    bpy.context.view_layer.update()
    show_only(roots, roots)
    scene = bpy.context.scene
    scene.render.film_transparent = False
    scene.world.node_tree.nodes.get("Background").inputs["Color"].default_value = (0.05, 0.055, 0.065, 1)
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    frame_camera(cam, [o for r in roots for o in hierarchy(r)], margin=1.04, aspect=1920 / 1080)
    render(os.path.join(RENDER_DIR, "lineup.png"))
    for r in roots:
        r.location = (0, 0, 0)

    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print("[rsc_art] done")


main()
