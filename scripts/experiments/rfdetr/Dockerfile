FROM nvidia/cuda:13.0.0-cudnn-runtime-ubuntu24.04
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-pip && rm -rf /var/lib/apt/lists/*
RUN python3 -m pip install --break-system-packages --no-cache-dir onnxruntime-gpu==1.32.0 opencv-python-headless
WORKDIR /work
ENTRYPOINT ["python3", "/work/scripts/experiments/rfdetr/probe.py"]
