"""Create an experimental FP16 ONNX copy; requires onnx and onnxconverter-common."""

import argparse

import numpy as np
import onnx
from onnx import numpy_helper
from onnxconverter_common.float16 import convert_float_to_float16

parser = argparse.ArgumentParser()
parser.add_argument("source")
parser.add_argument("destination")
parser.add_argument("--repair-cast-types", action="store_true")
args = parser.parse_args()

model = convert_float_to_float16(onnx.load(args.source), keep_io_types=True, op_block_list=[])
resize_inputs = {
    name
    for node in model.graph.node
    if node.op_type == "Resize"
    for name in node.input[1:3]
    if name
}
for tensor in model.graph.initializer:
    if tensor.name in resize_inputs and tensor.data_type == onnx.TensorProto.FLOAT16:
        tensor.CopyFrom(numpy_helper.from_array(numpy_helper.to_array(tensor).astype(np.float32), tensor.name))
for node in model.graph.node:
    if node.op_type == "Constant" and any(name in resize_inputs for name in node.output):
        for attribute in node.attribute:
            if attribute.type == onnx.AttributeProto.TENSOR and attribute.t.data_type == onnx.TensorProto.FLOAT16:
                attribute.t.CopyFrom(numpy_helper.from_array(numpy_helper.to_array(attribute.t).astype(np.float32)))
for value in model.graph.value_info:
    if value.name in resize_inputs:
        value.type.tensor_type.elem_type = onnx.TensorProto.FLOAT
if args.repair_cast_types:
    cast_types = {
        node.output[0]: next(attribute.i for attribute in node.attribute if attribute.name == "to")
        for node in model.graph.node
        if node.op_type == "Cast"
    }
    for value in model.graph.value_info:
        if value.name in cast_types:
            value.type.tensor_type.elem_type = cast_types[value.name]
onnx.save(model, args.destination)
