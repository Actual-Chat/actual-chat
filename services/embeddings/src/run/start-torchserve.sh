#!/bin/bash

echo "server is starting..."

model_file_path=$(find ./model_store -type f | head -n 1)
model_file_name=$(basename $model_file_path)
model_name="${model_file_name%.*}"

# --ncs means the snapshot feature is disabled.
torchserve --foreground --disable-token-auth --model-store ./model_store --models "${model_name}=${model_file_name}" --ncs
