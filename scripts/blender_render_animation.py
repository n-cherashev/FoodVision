import bpy
import math
import os
import subprocess

def render_cctv_animation():
    print("Building Animated 3D Restaurant CCTV Simulation...")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    
    # Render engine: EEVEE Next or EEVEE
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    
    # 1280x720 30 FPS, 300 frames (10 seconds)
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.fps = 30
    scene.frame_start = 1
    scene.frame_end = 300
    
    frames_dir = os.path.abspath("videos/render_frames")
    os.makedirs(frames_dir, exist_ok=True)
    
    scene.render.image_settings.file_format = 'JPEG'
    scene.render.image_settings.quality = 95
    scene.render.filepath = os.path.join(frames_dir, "frame_")
    
    # Materials
    def mat(name, color, rough=0.3, metal=0.0):
        m = bpy.data.materials.new(name=name)
        m.use_nodes = True
        b = m.node_tree.nodes.get('Principled BSDF')
        if b:
            b.inputs['Base Color'].default_value = color
            b.inputs['Roughness'].default_value = rough
            b.inputs['Metallic'].default_value = metal
        return m

    m_floor = mat("Floor", (0.85, 0.83, 0.80, 1.0), rough=0.18)
    m_wall = mat("Wall", (0.30, 0.32, 0.35, 1.0), rough=0.7)
    m_wood = mat("Wood", (0.42, 0.24, 0.12, 1.0), rough=0.25)
    m_chair = mat("Chair", (0.15, 0.15, 0.17, 1.0), rough=0.5)
    m_skin = mat("Skin", (0.86, 0.68, 0.58, 1.0), rough=0.6)
    m_hair = mat("Hair", (0.12, 0.08, 0.05, 1.0), rough=0.8)
    m_suit = mat("Suit", (0.12, 0.20, 0.38, 1.0), rough=0.5)
    m_sweater = mat("Sweater", (0.90, 0.70, 0.18, 1.0), rough=0.6)
    m_waiter = mat("Waiter", (0.05, 0.05, 0.05, 1.0), rough=0.4)
    m_plate = mat("Plate", (0.96, 0.96, 0.96, 1.0), rough=0.1)
    m_food = mat("Food", (0.65, 0.30, 0.15, 1.0), rough=0.5)
    m_metal = mat("Metal", (0.85, 0.87, 0.90, 1.0), rough=0.2, metal=0.9)

    # Floor & Walls
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(m_floor)
    
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 7.0, 2.5))
    bw = bpy.context.active_object
    bw.scale = (20.0, 0.2, 5.0)
    bw.data.materials.append(m_wall)

    # Main Dining Table T-03
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.75))
    table = bpy.context.active_object
    table.name = "Table_T03"
    table.scale = (1.3, 1.3, 0.06)
    table.data.materials.append(m_wood)

    bpy.ops.mesh.primitive_cylinder_add(radius=0.12, depth=0.72, location=(0, 0, 0.36))
    bpy.context.active_object.data.materials.append(m_chair)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.45, depth=0.03, location=(0, 0, 0.015))
    bpy.context.active_object.data.materials.append(m_chair)

    # Chairs
    for cx, crot in [(-0.85, 0.0), (0.85, math.radians(180))]:
        bpy.ops.mesh.primitive_cube_add(size=0.46, location=(cx, 0.0, 0.45))
        bpy.context.active_object.scale = (1.0, 1.0, 0.1)
        bpy.context.active_object.data.materials.append(m_chair)
        bpy.ops.mesh.primitive_cube_add(size=0.46, location=(cx * 1.15, 0.0, 0.75))
        b = bpy.context.active_object
        b.scale = (0.1, 0.9, 0.7)
        b.rotation_euler[2] = crot
        b.data.materials.append(m_chair)

    # Background dining tables
    for bx, by in [(-3.5, 3.0), (3.5, 3.0), (-3.5, -3.0)]:
        bpy.ops.mesh.primitive_cylinder_add(radius=0.65, depth=0.06, location=(bx, by, 0.75))
        bpy.context.active_object.data.materials.append(m_wood)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.1, depth=0.72, location=(bx, by, 0.36))
        bpy.context.active_object.data.materials.append(m_chair)

    # Food Plates on Table (appear when served at frame 120)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.20, depth=0.02, location=(-0.35, 0.0, 0.79))
    plate1 = bpy.context.active_object
    plate1.name = "Plate1"
    plate1.data.materials.append(m_plate)
    
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.10, location=(-0.35, 0.0, 0.84))
    food1 = bpy.context.active_object
    food1.scale = (1.0, 1.0, 0.4)
    food1.data.materials.append(m_food)
    food1.parent = plate1

    bpy.ops.mesh.primitive_cylinder_add(radius=0.20, depth=0.02, location=(0.35, 0.0, 0.79))
    plate2 = bpy.context.active_object
    plate2.name = "Plate2"
    plate2.data.materials.append(m_plate)

    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.10, location=(0.35, 0.0, 0.84))
    food2 = bpy.context.active_object
    food2.scale = (1.0, 1.0, 0.4)
    food2.data.materials.append(m_food)
    food2.parent = plate2

    for p in [plate1, plate2]:
        p.scale = (0, 0, 0)
        p.keyframe_insert(data_path="scale", frame=1)
        p.keyframe_insert(data_path="scale", frame=119)
        p.scale = (1, 1, 1)
        p.keyframe_insert(data_path="scale", frame=120)

    # Human Characters
    def make_person(name, pos, mat_cloth):
        bpy.ops.object.empty_add(type='PLAIN_AXES', location=pos)
        root = bpy.context.active_object
        root.name = name

        torso = bpy.ops.mesh.primitive_cube_add(size=0.5, location=(pos[0], pos[1], pos[2] + 0.95))
        torso_obj = bpy.context.active_object
        torso_obj.scale = (0.7, 0.45, 1.1)
        torso_obj.data.materials.append(mat_cloth)
        torso_obj.parent = root

        head = bpy.ops.mesh.primitive_uv_sphere_add(radius=0.16, location=(pos[0], pos[1], pos[2] + 1.55))
        head_obj = bpy.context.active_object
        head_obj.data.materials.append(m_skin)
        head_obj.parent = root

        hair = bpy.ops.mesh.primitive_uv_sphere_add(radius=0.17, location=(pos[0], pos[1], pos[2] + 1.62))
        hair_obj = bpy.context.active_object
        hair_obj.scale = (1.0, 1.0, 0.5)
        hair_obj.data.materials.append(m_hair)
        hair_obj.parent = root

        for ax in [-0.26, 0.26]:
            bpy.ops.mesh.primitive_cylinder_add(radius=0.07, depth=0.55, location=(pos[0] + ax, pos[1], pos[2] + 0.95))
            arm = bpy.context.active_object
            arm.data.materials.append(mat_cloth)
            arm.parent = root

        for lx in [-0.14, 0.14]:
            bpy.ops.mesh.primitive_cylinder_add(radius=0.08, depth=0.8, location=(pos[0] + lx, pos[1], pos[2] + 0.4))
            leg = bpy.context.active_object
            leg.data.materials.append(mat_cloth)
            leg.parent = root

        return root

    g1 = make_person("Guest1", (-0.85, 0.0, -0.45), m_suit)
    g2 = make_person("Guest2", (0.85, 0.0, -0.45), m_sweater)
    waiter = make_person("Waiter", (0.0, 4.0, 0.0), m_waiter)

    # Waiter Service Tray
    bpy.ops.mesh.primitive_cylinder_add(radius=0.30, depth=0.02, location=(0.0, 3.6, 1.05))
    tray = bpy.context.active_object
    tray.name = "WaiterTray"
    tray.data.materials.append(m_metal)
    tray.parent = waiter

    # Animation
    # Waiter walks to table
    waiter.location = (0.0, 4.0, 0.0)
    waiter.keyframe_insert(data_path="location", frame=1)
    waiter.location = (0.0, 1.15, 0.0)
    waiter.keyframe_insert(data_path="location", frame=90)
    waiter.location = (0.0, 1.15, 0.0)
    waiter.keyframe_insert(data_path="location", frame=150)
    waiter.location = (0.0, 4.2, 0.0)
    waiter.keyframe_insert(data_path="location", frame=240)
    waiter.location = (0.0, 4.2, 0.0)
    waiter.keyframe_insert(data_path="location", frame=300)

    # CCTV Camera
    cam_loc = (4.2, -4.2, 3.8)
    bpy.ops.object.camera_add(location=cam_loc)
    camera = bpy.context.active_object
    camera.name = "CCTV_Camera"
    camera.data.lens = 26
    scene.camera = camera

    tt = camera.constraints.new(type='TRACK_TO')
    tt.target = table
    tt.track_axis = 'TRACK_NEGATIVE_Z'
    tt.up_axis = 'UP_Y'

    # Lighting
    bpy.ops.object.light_add(type='SPOT', radius=1.0, location=(0, 0, 4.5))
    spot = bpy.context.active_object
    spot.data.energy = 850
    spot.data.color = (1.0, 0.95, 0.88)
    spot.data.spot_size = math.radians(75)

    bpy.ops.object.light_add(type='SUN', location=(5, -5, 6))
    sun = bpy.context.active_object
    sun.data.energy = 2.2
    sun.data.color = (0.95, 0.98, 1.0)
    sun.rotation_euler = (math.radians(45), math.radians(20), math.radians(45))

    print("Rendering 300 frames to JPEG sequence...")
    bpy.ops.render.render(animation=True)
    print("Blender frame rendering complete!")

if __name__ == "__main__":
    render_cctv_animation()
