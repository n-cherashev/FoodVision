import cv2
import numpy as np
import os

def create_restaurant_lifecycle_video():
    source_path = "videos/Cctv-Resturant-Demo.mp4"
    output_path = "videos/Cctv-Restaurant-Full-Service.mp4"
    
    cap = cv2.VideoCapture(source_path)
    fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    
    print(f"Reading source: {w}x{h}, {fps} fps, {total_frames} frames")
    
    # Pre-load all source frames into memory for ultra-fast composition
    raw_frames = []
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        raw_frames.append(frame)
    cap.release()
    print(f"Loaded {len(raw_frames)} frames into memory.")
    
    # Reference frame with clean empty table T-03
    f_empty_t3 = raw_frames[80].copy()
    
    # Table 3 bounding mask (soft feathered mask for seamless blending)
    # T-03 table surface and immediate surrounds: x: 880-1080, y: 310-440
    t3_mask = np.zeros((h, w), dtype=np.float32)
    t3_pts = np.array([[890, 310], [1060, 315], [1080, 440], [880, 440]], dtype=np.int32)
    cv2.fillPoly(t3_mask, [t3_pts], 1.0)
    t3_mask = cv2.GaussianBlur(t3_mask, (21, 21), 0)[:, :, np.newaxis]
    
    # Output frames collector
    out_frames = []
    
    # -------------------------------------------------------------
    # PHASE 1: Table Free (00:00 - 00:08) [240 frames]
    # Restaurant opens, Table T-03 is empty, waiter in background
    # -------------------------------------------------------------
    print("Generating Phase 1: Table Free...")
    for i in range(0, 240):
        out_frames.append(raw_frames[i])
        
    # -------------------------------------------------------------
    # PHASE 2: Guests Arrive and Sit (00:08 - 00:18) [300 frames]
    # Man & woman enter through door, walk to Table T-03, sit down
    # -------------------------------------------------------------
    print("Generating Phase 2: Guests Arrive & Sit...")
    for i in range(240, 540):
        out_frames.append(raw_frames[i])
        
    # -------------------------------------------------------------
    # PHASE 3: Waiter Approaches to Take Order (00:18 - 00:30) [360 frames]
    # Waiter walks from kitchen to Table T-03, stands by guests taking order,
    # then walks back to kitchen. Table remains without food!
    # -------------------------------------------------------------
    print("Generating Phase 3: Waiter Takes Order...")
    # 3a. Waiter walks from counter to T-03 (frames 640 to 710) [70 frames]
    for i in range(640, 710):
        # Blend empty table surface onto frame so no food appears on table
        frame = raw_frames[i].copy()
        # In frame 640-710, food is still on waiter tray, not on table
        out_frames.append(frame)
        
    # 3b. Waiter stands by Table T-03 interacting with guests (90 frames)
    # Loop subtle conversation motion (frames 705 to 715)
    for k in range(9):
        for i in range(705, 715):
            frame = raw_frames[i].copy()
            out_frames.append(frame)
            
    # 3c. Waiter turns around and walks back to counter (frames 765 to 825) [60 frames]
    # But table must NOT have food yet! Replace table surface with empty table surface
    for i in range(765, 825):
        frame = raw_frames[i].copy()
        blended = (frame * (1.0 - t3_mask) + f_empty_t3 * t3_mask).astype(np.uint8)
        out_frames.append(blended)
        
    # Remaining padding to 360 frames
    pad_count = 360 - (70 + 90 + 60)
    for i in range(pad_count):
        idx = 500 + (i % 40)
        out_frames.append(raw_frames[idx])
        
    # -------------------------------------------------------------
    # PHASE 4: Waiting for Food (00:30 - 00:40) [300 frames]
    # Guests seated talking and waiting. Table is empty.
    # -------------------------------------------------------------
    print("Generating Phase 4: Waiting for Food...")
    for i in range(300):
        idx = 480 + (i % 160)
        out_frames.append(raw_frames[idx])
        
    # -------------------------------------------------------------
    # PHASE 5: Waiter Delivers Food (Explicit Delivery) (00:40 - 00:54) [420 frames]
    # Waiter walks from kitchen with tray, delivers steaming dishes,
    # guests receive food, waiter walks back to counter.
    # -------------------------------------------------------------
    print("Generating Phase 5: Waiter Delivers Food...")
    # Walk from kitchen to table (frames 630 to 735) [105 frames]
    for i in range(630, 735):
        out_frames.append(raw_frames[i])
        
    # Serving food and presenting to guests (frames 735 to 755 repeated with subtle interaction) [60 frames]
    for _ in range(3):
        for i in range(735, 755):
            out_frames.append(raw_frames[i])
            
    # Waiter turns and walks back with empty tray (frames 755 to 830) [75 frames]
    for i in range(755, 830):
        out_frames.append(raw_frames[i])
        
    # Guests looking at food and starting meal (frames 830 to 1010) [180 frames]
    for i in range(830, 1010):
        out_frames.append(raw_frames[i])
        
    # -------------------------------------------------------------
    # PHASE 6: Guests Dining (00:54 - 01:14) [600 frames]
    # Guests actively eating food with utensils, dining and conversing.
    # -------------------------------------------------------------
    print("Generating Phase 6: Guests Dining...")
    for i in range(600):
        idx = 850 + (i % 450)
        out_frames.append(raw_frames[idx])
        
    # -------------------------------------------------------------
    # PHASE 7: Guests Finish, Stand Up & Leave (01:14 - 01:26) [360 frames]
    # Guests finish meal, stand up from chairs, walk towards exit.
    # Table T-03 is left with dirty plates.
    # -------------------------------------------------------------
    print("Generating Phase 7: Guests Leave...")
    # 7a. Finishing and pushing chairs back (frames 540 down to 420 reversed) [120 frames]
    # Reference frame with dirty dishes on table
    f_dirty_t3 = raw_frames[1200].copy()
    
    leave_frames = []
    for i in range(240, 500):
        frame = raw_frames[i].copy()
        # Keep dirty dishes on table while guests leave
        frame = (frame * (1.0 - t3_mask) + f_dirty_t3 * t3_mask).astype(np.uint8)
        leave_frames.append(frame)
        
    # Reverse to show them standing up and walking to doorway
    for f in reversed(leave_frames):
        out_frames.append(f)
        
    # Pad until 360 frames with empty table showing dirty dishes
    while len(out_frames) < (240 + 300 + 360 + 300 + 420 + 600 + 360):
        bg_frame = raw_frames[80].copy()
        blended = (bg_frame * (1.0 - t3_mask) + f_dirty_t3 * t3_mask).astype(np.uint8)
        out_frames.append(blended)
        
    # -------------------------------------------------------------
    # PHASE 8: Waiter Clears & Cleans Table (01:26 - 01:40) [420 frames]
    # Waiter walks to Table T-03, clears dirty dishes, wipes table,
    # walks away. Table becomes clean and spotless!
    # -------------------------------------------------------------
    print("Generating Phase 8: Waiter Cleans Table...")
    # 8a. Waiter walks from counter to dirty table (frames 640 to 710) [70 frames]
    for i in range(640, 710):
        frame = raw_frames[i].copy()
        blended = (frame * (1.0 - t3_mask) + f_dirty_t3 * t3_mask).astype(np.uint8)
        out_frames.append(blended)
        
    # 8b. Waiter wiping and clearing dishes (frames 710 to 750, transitioning table from dirty to clean) [120 frames]
    for k in range(3):
        alpha_clean = float(k) / 2.0  # progressively clean
        for i in range(710, 750):
            frame = raw_frames[i].copy()
            table_state = (f_dirty_t3 * (1.0 - alpha_clean) + f_empty_t3 * alpha_clean).astype(np.uint8)
            blended = (frame * (1.0 - t3_mask) + table_state * t3_mask).astype(np.uint8)
            out_frames.append(blended)
            
    # 8c. Waiter walks away with tray to kitchen (frames 760 to 830) [70 frames]
    for i in range(760, 830):
        frame = raw_frames[i].copy()
        blended = (frame * (1.0 - t3_mask) + f_empty_t3 * t3_mask).astype(np.uint8)
        out_frames.append(blended)
        
    # 8d. Clean table ready for next guests (frames 80 to 240) [160 frames]
    for i in range(80, 240):
        out_frames.append(raw_frames[i])
        
    print(f"Total compiled frames: {len(out_frames)}")
    
    # Write to MP4 using mp4v codec
    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    out = cv2.VideoWriter(output_path, fourcc, fps, (w, h))
    for f in out_frames:
        out.write(f)
    out.release()
    print(f"Saved compiled video to: {output_path}")

if __name__ == "__main__":
    create_restaurant_lifecycle_video()
