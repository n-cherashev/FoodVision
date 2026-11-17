# FoodVision-cs

Минимальный CPU-бенчмарк: OpenCV читает видео, каждый кадр приводится к 640x640 RGB CHW `/ 255`, затем выполняется ONNX Runtime inference. Программа использует `float16`, поскольку приложенный YOLOv5n ONNX ожидает этот тип. Обработка результатов модели намеренно отсутствует.

```powershell
dotnet run -c Release -- <видео> <модель.onnx> 10 10
```

Пример:

```powershell
dotnet run -c Release -- ..\assets\video.mp4 ..\assets\yolov5n.onnx 10 10

Последние два аргумента — число проходов на worker и число одновременных workers. Каждый worker использует собственные ONNX-сессию, `VideoCapture` и буферы; итоговый `aggregate_fps` измеряет суммарную пропускную способность.
```
