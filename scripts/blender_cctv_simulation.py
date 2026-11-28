import bpy
import math
import os

def build_and_render_cctv():
    print("Initializing Blender 5.3 CCTV Simulation...")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    
    # Check render engines
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    if 'BLENDER_EEVEE_NEXT' in engines:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    elif 'BLENDER_EEVEE' in engines:
        scene.render.engine = 'BLENDER_EEVEE'
    else:
        scene.render.engine = 'CYCLES'
    
    # 1280x720 30 FPS
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.fps = 30
    
    # Output path
    output_dir = os.path.abspath("videos")
    os.makedirs(output_dir, exist_ok=True)
    
    # Material helper
    def make_material(name, color, roughness=0.3, metallic=0.0):
        mat = bpy.data.materials.new(name=name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        if bsdf:
            bsdf.inputs['Base Color'].default_value = color
            bsdf.inputs['Roughness'].default_value = roughness
            bsdf.inputs['Metallic'].default_value = metallic
        return mat

    mat_floor = make_material("MatFloor", (0.85, 0.83, 0.80, 1.0), roughness=0.18)
    mat_wall = make_material("MatWall", (0.32, 0.34, 0.36, 1.0), roughness=0.7)
    mat_wood = make_material("MatWood", (0.42, 0.25, 0.12, 1.0), roughness=0.25)
    mat_chair = make_material("MatChair", (0.15, 0.15, 0.17, 1.0), roughness=0.5)
    mat_skin = make_material("MatSkin", (0.88, 0.68, 0.58, 1.0), roughness=0.6)
    mat_guest1 = make_material("MatGuest1", (0.12, 0.22, 0.42, 1.0), roughness=0.5) # Blue jacket
    mat_guest2 = make_material("MatGuest2", (0.90, 0.70, 0.18, 1.0), roughness=0.6) # Yellow sweater
    mat_waiter = make_material("MatWaiter", (0.05, 0.05, 0.05, 1.0), roughness=0.4) # Black uniform
    mat_plate = make_material("MatPlate", (0.96, 0.96, 0.96, 1.0), roughness=0.1)
    mat_food = make_material("MatFood", (0.60, 0.32, 0.12, 1.0), roughness=0.5)
    mat_metal = make_material("MatMetal", (0.85, 0.87, 0.90, 1.0), roughness=0.2, metallic=0.95)

    # Architecture
    # Floor (large tiles)
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, 0))
    floor = bpy.context.active_object
    floor.name = "Floor"
    floor.data.materials.append(mat_floor)

    # Back Wall
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 7.0, 2.5))
    back_wall = bpy.context.active_object
    back_wall.name = "BackWall"
    back_wall.scale = (20.0, 0.2, 5.0)
    back_wall.data.materials.append(mat_wall)

    # Left Wall
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-7.0, 0, 2.5))
    left_wall = bpy.context.active_object
    left_wall.name = "LeftWall"
    left_wall.scale = (0.2, 20.0, 5.0)
    left_wall.data.materials.append(mat_wall)

    # Main Dining Table (T-03)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.75))
    table = bpy.context.active_object
    table.name = "Table_T03"
    table.scale = (1.3, 1.3, 0.06)
    table.data.materials.append(mat_wood)

    # Table Base Pedestal
    bpy.ops.mesh.primitive_cylinder_add(radius=0.12, depth=0.72, location=(0, 0, 0.36))
    pedestal = bpy.context.active_object
    pedestal.name = "TablePedestal"
    pedestal.data.materials.append(mat_chair)

    # Base Plate on Floor
    bpy.ops.mesh.primitive_cylinder_add(radius=0.45, depth=0.03, location=(0, 0, 0.015))
    base_plate = bpy.context.active_object
    base_plate.data.materials.append(mat_chair)

    # Chairs around T-03
    def make_chair(name, x, y, rot_z):
        bpy.ops.mesh.primitive_cube_add(size=0.46, location=(x, y, 0.45))
        seat = bpy.context.active_object
        seat.name = f"{name}_Seat"
        seat.scale = (1.0, 1.0, 0.1)
        seat.data.materials.append(mat_chair)
        
        bpy.ops.mesh.primitive_cube_add(size=0.46, location=(x + 0.2 * math.cos(rot_z), y + 0.2 * math.sin(rot_z), 0.75))
        back = bpy.context.active_object
        back.name = f"{name}_Back"
        back.scale = (0.1, 0.9, 0.7)
        back.rotation_euler[2] = rot_z
        back.data.materials.append(mat_chair)

    make_chair("Chair_Left", -0.85, 0.0, 0.0)
    make_chair("Chair_Right", 0.85, 0.0, math.radians(180))

    # Background dining tables for depth
    for bx, by in [(-3.5, 3.0), (3.5, 3.0), (-3.5, -3.0)]:
        bpy.ops.mesh.primitive_cylinder_add(radius=0.65, depth=0.06, location=(bx, by, 0.75))
        bt = bpy.context.active_object
        bt.data.materials.append(mat_wood)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.1, depth=0.72, location=(bx, by, 0.36))
        bpy.context.active_object.data.materials.append(mat_chair)

    # Food Plates on Table
    bpy.ops.mesh.primitive_cylinder_add(radius=0.20, depth=0.02, location=(-0.35, 0.0, 0.79))
    plate1 = bpy.context.active_object
    plate1.name = "Plate1"
    plate1.data.materials.append(mat_plate)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.10, location=(-0.35, 0.0, 0.84))
    food1 = bpy.context.active_object
    food1.name = "Food1"
    food1.scale = (1.0, 1.0, 0.4)
    food1.data.materials.append(mat_food)

    bpy.ops.mesh.primitive_cylinder_add(radius=0.20, depth=0.02, location=(0.35, 0.0, 0.79))
    plate2 = bpy.context.active_object
    plate2.name = "Plate2"
    plate2.data.materials.append(mat_plate)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.10, location=(0.35, 0.0, 0.84))
    food2 = bpy.context.active_object
    food2.name = "Food2"
    food2.scale = (1.0, 1.0, 0.4)
    food2.data.materials.append(mat_food)

    # Waiter Service Tray
    bpy.ops.mesh.primitive_cylinder_add(radius=0.30, depth=0.02, location=(0, 1.2, 1.05))
    tray = bpy.context.active_object
    tray.name = "WaiterTray"
    tray.data.materials.append(mat_metal)

    # Character Creation (Proportional human figures recognized by YOLO)
    def create_character(name, base_pos, mat_body, is_seated=False):
        # Empty root
        bpy.ops.object.empty_add(type='PLAIN_AXES', location=base_pos)
        root = bpy.context.active_object
        root.name = name

        torso_z = 0.55 if is_seated else 1.0
        head_z = 1.05 if is_seated else 1.55
        
        # Torso
        bpy.ops.mesh.primitive_cube_add(size=0.5, location=(base_pos[0], base_pos[1], base_pos[2] + torso_z))
        torso = bpy.context.active_object
        torso.name = f"{name}_Torso"
        torso.scale = (0.7, 0.45, 1.1)
        torso.data.materials.append(mat_body)
        torso.parent = root

        # Head
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.16, location=(base_pos[0], base_pos[1], base_pos[2] + head_z))
        head = bpy.context.active_object
        head.name = f"{name}_Head"
        head.data.materials.append(mat_skin)
        head.parent = root

        # Arms
        arm_z = torso_z
        bpy.ops.mesh.primitive_cylinder_add(radius=0.07, depth=0.55, location=(base_pos[0] - 0.26, base_pos[1], base_pos[2] + arm_z))
        arm_l = bpy.context.active_object
        arm_l.name = f"{name}_ArmL"
        arm_l.data.materials.append(mat_body)
        arm_l.parent = root

        bpy.ops.mesh.primitive_cylinder_add(radius=0.07, depth=0.55, location=(base_pos[0] + 0.26, base_pos[1], base_pos[2] + arm_z))
        arm_r = bpy.context.active_object
        arm_r.name = f"{name}_ArmR"
        arm_r.data.materials.append(mat_body)
        arm_r.parent = root

        # Legs (if standing)
        if not is_seated:
            for lx in [-0.14, 0.14]:
                bpy.ops.mesh.primitive_cylinder_add(radius=0.08, depth=0.8, location=(base_pos[0] + lx, base_pos[1], base_pos[2] + 0.4))
                leg = bpy.context.active_object
                leg.data.materials.append(mat_body)
                leg.parent = root

        return root

    # Seated Guests at T-03
    guest1 = create_character("Guest1", (-0.85, 0.0, 0.0), mat_guest1, is_seated=True)
    guest2 = create_character("Guest2", (0.85, 0.0, 0.0), mat_guest2, is_seated=True)

    # Waiter standing beside table serving food
    waiter = create_character("Waiter", (0.0, 1.2, 0.0), mat_waiter, is_seated=False)

    # Lighting
    # Spotlight directly over Table T-03
    bpy.ops.object.light_add(type='SPOT', radius=1.0, location=(0, 0, 4.5))
    spot = bpy.context.active_object
    spot.name = "TableSpot"
    spot.data.energy = 800
    spot.data.color = (1.0, 0.95, 0.88)
    spot.data.spot_size = math.radians(70)

    # Ambient soft fill light
    bpy.ops.object.light_add(type='SUN', location=(4, -4, 5))
    sun = bpy.context.active_object
    sun.data.energy = 2.0
    sun.data.color = (0.96, 0.98, 1.0)
    sun.rotation_euler = (math.radians(45), math.radians(20), math.radians(45))

    # CCTV Surveillance Camera (mounted in ceiling corner)
    bpy.ops.object.camera_add(location=(4.5, -4.5, 3.8))
    camera = bpy.context.active_object
    camera.name = "CCTV_Camera"
    camera.data.lens = 28 # Wide-angle security lens
    scene.camera = camera

    # Point directly at Table T-03 center using Track To constraint
    tt = camera.constraints.new(type='TRACK_TO')
    tt.target = table
    tt.track_axis = 'TRACK_NEGATIVE_Z'
    tt.up_axis = 'UP_Y'

    print("Setup successfully finished. Rendering test frame...")
    test_out = os.path.join(output_dir, "blender_cctv_test_frame.jpg")
    scene.render.filepath = test_out
    bpy.ops.render.render(write_still=True)
    print(f"Rendered test frame to: {test_out}")

if __name__ == "__main__":
    build_and_render_cctv()
