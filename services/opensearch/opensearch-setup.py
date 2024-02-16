import os
import requests
import time
from opensearchpy import OpenSearch
from opensearch_py_ml.ml_commons import MLCommonClient


class API:
    def __init__(self, cluster_url):
        self._cluster_url = cluster_url

    def call_opensearch(self, path, *, method=requests.get, data=None):
        headers = {
            'Content-Type': 'application/json',
        }
        return method(
            self._cluster_url + path,
            headers=headers,
            json=data,
        )

    def register_model_group(self, model_group_name, *, cluster_url, description=""):
        # Notes:
        # For some reason current opensearch_py_ml client
        # does not have methods to register a model group
        try:
            # Return an existing model group id if it already exists.
            return self.call_opensearch(
                "/_plugins/_ml/model_groups/_search",
                data = {
                    "query": {
                        "match": {
                            "name": model_group_name
                        }
                    }
                }
            ).json()['hits']['hits'][0]['_id']
        except KeyError:
            # Python EAFP principle
            pass
        return self.call_opensearch(
            "/_plugins/_ml/model_groups/_register",
            method = requests.post,
            data = {
                "name": model_group_name,
                "description": description
            }
        ).json()['model_group_id']

    def set_ml_commons_config(self, config):
        return self.call_opensearch(
            "/_cluster/settings",
            method = requests.put,
            data = {
                "persistent": {
                    "plugins": {
                        "ml_commons": config
                    }
                }
            }
        ).json()

def main():
    cluster_url = os.getenv('OPENSEARCH_CLUSTER_URL')
    api = API(cluster_url)
    api.set_ml_commons_config({
        "only_run_on_ml_node": "true",
        "model_access_control_enabled": "true",
        "native_memory_threshold": "99",
        # This is a requirement to upload ML model from a local storage
        "allow_registering_model_via_local_file": "true",
    })
    model_group_id = api.register_model_group(
        'NLP_model_group',
        description = "A model group for NLP models",
        cluster_url = cluster_url
    )
    client = OpenSearch(
        hosts=[cluster_url],
    )
    ml_client = MLCommonClient(client)
    model_id = ml_client.register_model(
        model_group_id = model_group_id,
        model_path = os.getenv('TORCHSCRIPT_MODEL_PATH'),
        model_config_path = os.getenv('TORCHSCRIPT_MODEL_CONFIG_PATH'),
        deploy_model = True,
        wait_until_deployed = True
    )
    print(model_id)

if __name__ == '__main__':
    try:
        sleep_on_failure = int(os.getenv('SLEEP_ON_FAILURE_SECONDS', 0))
    except ValueError:
        sleep_on_failure = 0
    try:
        main()
    except:
        if sleep_on_failure > 0:
            print("Sleep on failure: %s seconds." % sleep_on_failure)
            time.sleep(sleep_on_failure)
        raise
