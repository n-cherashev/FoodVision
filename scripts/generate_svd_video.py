import torch
import cv2
import numpy as np
from PIL import Image
from diffusers import StableVideoDiffusionPipeline
from diffusers.utils import export_to_video

def main():
    print("Checking CUDA and device capabilities...")
    assert torch.cuda.is_available(), "CUDA must be available"
    device = "cuda"
    gpu_name = torch.cuda.get_device_name(0)
    vram_gb = torch.cuda.get_device_properties(0).total_memory / (1024**3)
    print(f"Device: {gpu_name} ({vram_gb:.1f} GB VRAM)")

    # 1. Load and prepare input image
    image_path = "videos/restaurant_cctv_base.jpg"
    print(f"Loading input image: {image_path}")
    input_img = Image.open(image_path).convert("RGB")
    
    # SVD native resolution: 1024 x 576
    input_img = input_img.resize((1024, 576), Image.Resampling.LANCZOS)
    print(f"Resized image to {input_img.size} (16:9)")

    # 2. Load Stable Video Diffusion XT 1.1 model
    model_id = "stabilityai/stable-video-diffusion-img2vid-xt-1-1"
    print(f"Loading pipeline: {model_id} (fp16)...")
    pipe = StableVideoDiffusionPipeline.from_pretrained(
        model_id,
        torch_dtype=torch.float16,
        variant="fp16",
    )
    
    # With 16 GB VRAM, enable sequential or model cpu offload to be 100% memory safe
    pipe.enable_model_cpu_offload()
    print("Pipeline loaded successfully into GPU memory.")

    # 3. Generate video frames
    # motion_bucket_id: 127 (moderate natural motion)
    # decode_chunk_size: 4 or 8 to keep VRAM low
    generator = torch.manual_seed(42)
    print("Starting AI video diffusion generation (25 frames)...")
    frames = pipe(
        input_img,
        decode_chunk_size=4,
        generator=generator,
        motion_bucket_id=140,
        fps=7,
        num_inference_steps=25,
    ).frames[0]

    print(f"Generated {len(frames)} frames!")

    # 4. Export to MP4
    output_raw = "videos/Ai-Generated-Restaurant-Service-raw.mp4"
    output_final = "videos/Ai-Generated-Restaurant-Service.mp4"
    export_to_video(frames, output_raw, fps=7)
    print(f"Raw video saved to {output_raw}")

    # 5. Interpolate / smooth to 24 FPS with browser-compatible H.264
    import imageio_ffmpeg
    import subprocess
    ffmpeg_bin = imageio_ffmpeg.get_ffmpeg_exe()
    
    # Convert and smooth to 24 fps
    cmd = [
        ffmpeg_bin, "-y",
        "-r", "7",
        "-i", output_raw,
        "-filter:v", "minterpolate='fps=24:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1'",
        "-c:v", "libx264",
        "-pix_fmt", "yuv420p",
        "-preset", "fast",
        "-crf", "20",
        "-movflags", "+faststart",
        output_final
    ]
    print("Running frame interpolation and H.264 re-encoding...")
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode == 0:
        print(f"Successfully created final AI video: {output_final}")
    else:
        # Fallback without minterpolate
        print("Fallback simple re-encode...")
        cmd_fallback = [
            ffmpeg_bin, "-y",
            "-i", output_raw,
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            output_final
        ]
        subprocess.run(cmd_fallback, check=True)
        print(f"Saved fallback video to: {output_final}")

if __name__ == "__main__":
    main()
