## Elastic (Failed)
docker compose -f services/elastic.compose.yaml up
---
git clone https://github.com/elastic/eland
cd eland
docker build run -it --rm --network="host" bash
eland_import_hub_model \
  --url http://elastic:elastic@localhost:9200/ \
  --hub-model-id sentence-transformers/msmarco-MiniLM-L-12-v3 \
  --task-type text_embedding \
  --start
(!) STOP: Requires Platinum lisence!

## OpenSearch
Guide followed: https://opensearch.org/docs/latest/search-plugins/neural-search-tutorial/

docker compose -f services/opensearch.compose.yaml up

curl -XPUT "http://localhost:9200/_cluster/settings" -H 'Content-Type: application/json' -d'
{
  "persistent": {
    "plugins": {
      "ml_commons": {
        "only_run_on_ml_node": "true",
        "model_access_control_enabled": "true",
        "native_memory_threshold": "99"
      }
    }
  }
}'


curl -XPOST "http://localhost:9200/_plugins/_ml/model_groups/_register" -H 'Content-Type: application/json' -d'
{
  "name": "NLP_model_group",
  "description": "A model group for NLP models"
}'

Note: result: model_group_id. HEc0o40Bd0X-N613U47u in this case.

curl -XPOST "http://localhost:9200/_plugins/_ml/models/_register" -H 'Content-Type: application/json' -d'
{
  "name": "huggingface/sentence-transformers/msmarco-distilbert-base-tas-b",
  "version": "1.0.1",
  "model_group_id": "HEc0o40Bd0X-N613U47u",
  "model_format": "TORCH_SCRIPT"
}'

NOTE: this task has only 2 states: CREATED -> COMPLETED





