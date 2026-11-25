# Альтернативные модели для video analytics

24 ноября 2026. Исследование основано на первичной документации и репозиториях авторов. Это перечень кандидатов, а не рекомендация к замене: опубликованные COCO-метрики и latency получены на иных сценах, разрешениях, GPU и runtime, поэтому не предсказывают точность или производительность FoodVision.

## Требования текущего алгоритма

Нужны четыре типа сигналов: люди с рамками, 17 ключевых точек тела, столы, а также `backpack`, `handbag` и `suitcase`. Последние четыре класса присутствуют в COCO как `dining table` (60), `backpack` (24), `handbag` (26) и `suitcase` (28), поэтому COCO detector способен дать нужные рамки. Позы нужны только для людей, чтобы определить посадку.

Среди проверенных готовых COCO checkpoint не найдено модели, которая выдаёт все эти результаты в одном вызове: рассмотренные keypoint checkpoint дают человека с позой, но не 80 COCO-классов; рассмотренные detector checkpoint дают 80 COCO-классов, но не keypoints. Возможный путь к одному вызову — обучение и проверка собственной multi-task модели.

## Кандидаты

| Кандидат | Готовый вес и выход | Что может заменить | Лицензия и интеграция | Главные ограничения |
| --- | --- | --- | --- | --- |
| **YOLO26 detect** (`n`…`x`) | Официальные COCO-веса: 80 object classes; ONNX и TensorRT export поддерживаются. | `yolo11x.onnx` для столов и вещей, а при достаточном recall — кандидат для person boxes. | Ultralytics: AGPL-3.0 либо Enterprise для закрытого production. ONNX можно выполнять через текущий ONNX Runtime C#. | Не возвращает keypoints. Меньшая модель может ухудшить людей, столы или вещи в ресторанной сцене. |
| **YOLO26-pose** (`n`…`x`) | Официальные COCO-pose веса: person box + 17 COCO keypoints. В документации: `n` 2.9M / 7.6B FLOPs, `s` 10.4M / 24.1B, `m` 21.5M / 73.3B, `l` 25.9M / 91.7B, `x` 57.6M / 202.3B. | `yolo11x-pose.onnx`; person box из той же pose-модели позволяет проверить отказ от отдельного YOLO26l detector. | Та же AGPL-3.0/Enterprise модель. ONNX export задокументирован. | Не детектирует столы/вещи. Меньшие варианты требуют сравнения качества определения посадки, а не только COCO OKS. |
| **RTMO** (MMPose) | One-stage multi-person pose: выдаёт pose detections, то есть person boxes + keypoints в одном pose-вызове. MMDeploy заявляет ONNX Runtime и TensorRT поддержку. | Сразу две текущие person-модели: `yolo11x-pose.onnx` и `yolo26l-detect.onnx`. Оставляет detector столов/вещей отдельным. | MMPose код Apache-2.0. Требуется свой adapter к выходу и ONNX export или официальная deployment-модель. | Лицензия кода не доказывает условия конкретных весов и датасетов; проверить карточку checkpoint до коммерческого использования. Нет таблиц и вещей. |
| **RTMPose** (MMPose) | Top-down person pose; официальные COCO веса и export в ONNX/TensorRT. RTMPose-t COCO checkpoint опубликован как 256×192. | Только pose-модель. В паре с detector людей это всё ещё два person-вызова. | Код MMPose Apache-2.0; требования для весов надо проверять отдельно. | Требует person boxes на входе; сам по себе не убирает detector людей. Опубликованная скорость зависит от выбранного detector. |
| **RF-DETR Keypoint (Preview)** | COCO pretrained checkpoint: person boxes + 17 keypoints; заявлены 71.8 COCO AP50:95, 9.7 ms на T4 TensorRT FP16, 576×576. | Обе person-модели в одном вызове, сохранив отдельную object-модель. | По документации Roboflow пакет и Keypoint Preview — Apache-2.0. Есть ONNX export; ONNX runtime возвращает raw tensors, нужен C# decoder. | Это early-access preview: API и checkpoint могут меняться. Значения latency не сопоставимы с текущими 640×640 ONNX Runtime CUDA измерениями. Не детектирует столы/вещи. |
| **RF-DETR detect Nano–Large** | COCO pretrained weights: 80 classes, включая требуемые объекты; weights скачиваются автоматически при создании модели. | `yolo11x.onnx` для столов/вещей и возможный источник person boxes. | Nano–Large и пакет Apache-2.0; ONNX/TensorRT export документирован. | Это отдельная модель от keypoint preview. Atto/Femto/Pico/XL/2XL и NAS используют PML 1.0: закрытый self-hosted production требует Enterprise add-on. |

## Наиболее полезные эксперименты

1. **RTMO против пары pose + YOLO26l.** Это кандидат, который архитектурно может сократить два текущих person-вызова до одного без замены object detector. Сначала экспортировать официальный checkpoint, написать адаптер и измерить на тех же 10, 20, 27, 30 потоках.
2. **RF-DETR Keypoint Preview против пары pose + YOLO26l.** Проверить как самостоятельный кандидат с более простой лицензией, но держать его в experimental lane из-за статуса Preview и нового декодера.
3. **YOLO26n/s-pose как дешёвая pose-модель.** Это наименьшее изменение текущего ONNX/C# пути. Сопоставить ключевые точки и итоговое состояние стола с baseline; не выбирать по COCO mAP или размеру файла.
4. **RF-DETR Small/Nano или YOLO26 detect для object detector.** Проверить table/belongings recall отдельно от pose. Этот вариант не сокращает число вызовов без успешной замены person pipeline.

## Что нужно измерять для каждого кандидата

- Совпадение person boxes и hip/knee keypoints с ручной разметкой в seating zones.
- Precision/recall для `dining table`, `backpack`, `handbag`, `suitcase` только в полезной области кадра.
- Ошибки и задержка переходов `seated`/`standing`, затем итоговые `TableState` и границы сегментов timeline.
- Тот же 640×360 source, 1 analytics FPS, текущий GPU concurrency=10, 10/20/27/30 потоков; p95 latency, late, dropped, VRAM и GPU utilization.
- Для ONNX: равенство результатов исходного checkpoint и экспортированного графа на фиксированных кадрах до нагрузочного теста.

## Лицензия и совместимость

- У Ultralytics AGPL-3.0 распространяется на их software repository; их документация предлагает Enterprise licence для closed-source production. Перед заменой следует подтвердить, что способ распространения FoodVision соответствует выбранной лицензии.
- Для MMPose Apache-2.0 относится к проекту. Условия весов и обучающих наборов не следует выводить только из лицензии кода: документировать конкретный URL, checksum и условия выбранного checkpoint.
- Для RF-DETR документация прямо относит Nano–Large, Seg и Keypoint Preview к Apache-2.0. Не использовать Plus/NAS варианты для self-hosted commercial deployment без отдельной проверки PML/Enterprise.
- Текущий C# runtime принимает ONNX. Ultralytics и RF-DETR документируют ONNX export, а MMPose/MMDeploy заявляют ONNX Runtime и TensorRT для RTMO/RTMPose. Каждый вариант всё равно потребует нового pre/post-processing и decoder; нельзя подставить вес в текущий `YoloOnnxModel` без проверки формата выходов.

## Первичные источники

- [Ultralytics COCO detection](https://docs.ultralytics.com/datasets/detect/coco/) — 80 COCO classes и official training/validation path.
- [Ultralytics YOLO26 pose](https://docs.ultralytics.com/tasks/pose/) — official pose variants, COCO keypoints, published quality/latency and output structure.
- [Ultralytics export](https://docs.ultralytics.com/modes/export/) — ONNX/TensorRT formats and output compatibility constraints.
- [Ultralytics licence statement](https://github.com/ultralytics/ultralytics/blob/main/README.md#-license) — AGPL-3.0 and Enterprise options.
- [MMPose RTMO announcement](https://github.com/open-mmlab/mmpose/releases/tag/v1.3.0) — RTMO described as a one-stage real-time multi-person pose method.
- [MMDeploy MMPose support matrix](https://github.com/open-mmlab/mmdeploy/blob/main/docs/en/04-supported-codebases/mmpose.md) — ONNX Runtime/TensorRT support for RTMO and RTMPose.
- [MMPose RTMPose COCO model zoo](https://github.com/open-mmlab/mmpose/blob/main/configs/body_2d_keypoint/rtmpose/coco/rtmpose_coco.yml) — official weights, configurations and COCO metrics.
- [MMPose repository licence](https://github.com/open-mmlab/mmpose#license) — Apache-2.0 project licence.
- [RF-DETR pretrained models](https://github.com/roboflow/rf-detr/blob/develop/docs/learn/pretrained.md) — detector classes, keypoint checkpoint and automatic weight download.
- [RF-DETR Keypoint Preview](https://github.com/roboflow/rf-detr/blob/develop/docs/learn/run/keypoints.md) — 17 keypoints, published benchmark, Preview status and output fields.
- [RF-DETR ONNX inference](https://github.com/roboflow/rf-detr/blob/develop/docs/exports/onnx.md) — raw ONNX outputs and runtime decoding.
- [RF-DETR licensing and model families](https://github.com/roboflow/rf-detr/blob/develop/docs/index.md) — Apache-2.0 and PML split.
