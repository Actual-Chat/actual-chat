#!/bin/bash

model_name=$1
handler=$2

data_folder="./data/${model_name}"

model_file="${data_folder}/model/model.safetensors"
config_file="${data_folder}/model/config.json"

# Initialize an empty variable to store the extra files paths
extra_files=""

# Add all files except the model_file
for file in $(find "$data_folder" -type f -not -path "$model_file"); do
    extra_files+="$file,"
done

# Remove the trailing comma
extra_files=${extra_files%,}

# Compute model hash & extract embedding dimension
model_hash=$(sha256sum "${model_file}" | cut -c1-16)
embedding_dimension=$(jq ".hidden_size" "${config_file}")

archived_model_name="${model_name/'/'/'_'}.${embedding_dimension}_${model_hash}"

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
