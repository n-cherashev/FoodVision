# FoodVision-py

Минимальный CPU benchmark: каждый кадр читается из видео, приводится к RGB 640×640,
масштабируется в диапазон 0..1, преобразуется из HWC в NCHW и подаётся в ONNX Runtime.
Результаты детектора намеренно не обрабатываются.

Используется Python 3.14.10: это самая новая стабильная версия Python с Windows-wheel для
ONNX Runtime 1.32.0. Создайте изолированное окружение и запустите benchmark:

```powershell
py -3.14 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe benchmark.py data/sample.avi data/yolov5n.onnx 10
```

Скрипт фиксирует один поток OpenCV и по одному intra/inter-op потоку ONNX Runtime.
Это важно повторить в C# варианте. У текущей модели вход `float16`: данные сначала
готовятся в `float32`, затем приводятся к входному типу модели непосредственно перед inference.
