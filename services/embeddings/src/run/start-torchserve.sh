#!/bin/bash

echo "Server is starting..."

# Grab path to the first file in the model_store dir. That must be the only file in the dir.
model_file_path=$(find ./model_store -type f | head -n 1)
# Extract file name from path
model_file_name=$(basename $model_file_path)
# Strip file extension & model dimension and hash to obtain model name
model_name="${model_file_name%%.*}"

# --ncs means the snapshot feature is disabled.
torchserve --foreground --disable-token-auth --model-store ./model_store --models "${model_name}=${model_file_name}" --ncs
