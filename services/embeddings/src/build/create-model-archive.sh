#!/bin/bash

model_name=$1
handler=$2

archived_model_name="${model_name/'/'/'_'}"
data_folder="./data/${model_name}"

# Model file is the largest file in the model folder
model_file=$(find "${data_folder}/model" -type f -exec du -b {} + | sort -nr | head -n 1 | awk '{print $2}')

# Initialize an empty variable to store the extra files paths
extra_files=""

# Add all files except the model_file
for file in $(find "$data_folder" -type f -not -path "$model_file"); do
    extra_files+="$file,"
done

# Remove the trailing comma
extra_files=${extra_files%,}

echo "Archived model name is: $archived_model_name"

echo "The model file is: $model_file"

echo "Extra file paths: $extra_files"

rm -rf model_store
mkdir -p model_store

torch-model-archiver \
--model-name "$archived_model_name" \
--version 1.0 \
--handler "$handler"  \
--model-file "$model_file" \
--extra-files "$extra_files" \
--export-path model_store
