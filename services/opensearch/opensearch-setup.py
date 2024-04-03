import os
import json
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
        result = method(
            self._cluster_url + path,
            headers=headers,
            json=data,
        )
        print(path)
        print(result)
        print(result.json())
        return result

    def register_model_group(self, model_group_name, *, cluster_url, description=""):
        # Notes:
        # For some reason current opensearch_py_ml client
        # does not have methods to register a model group
        try:
            # Return an existing model group id if it already exists.
            return self.call_opensearch(
                "/_plugins/_ml/model_groups/_search",
                method = requests.post,
                data = {
                    "query": {
                        "match": {
                            "name": model_group_name
                        }
                    }
                }
            ).json()['hits']['hits'][0]['_id']
        except (KeyError, IndexError):
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

    def get_model_group_model_id(self, model_group_id, *, cluster_url):
        # Notes:
        # For some reason current opensearch_py_ml client
        # does not have methods to register a model group
        try:
            # Return an existing model group id if it already exists.
            response = self.call_opensearch(
                "/_plugins/_ml/models/_search",
                method = requests.post,
                data = {
                    "query": {
                        "match": {
                            "model_group_id": model_group_id
                        }
                    },
                    "sort": [{
                        "_seq_no": { "order": "desc" }
                    }],
                    "size": 1
                }
            )
            if response.status_code == 404:
                # .plugin ml index not found error.
                return None
            return response.json()['hits']['hits'][0]['_id']
        except IndexError:
            # Python EAFP principle
            return None


def main():
    cluster_url = os.getenv('OPENSEARCH_CLUSTER_URL')
    model_group_name = os.getenv('OPENSEARCH_ML_MODEL_GROUP')
    api = API(cluster_url)
    api.set_ml_commons_config({
        "only_run_on_ml_node": "false",
        "model_access_control_enabled": "true",
        "native_memory_threshold": "99",
        # This is a requirement to upload ML model from a local storage
        "allow_registering_model_via_local_file": "true",
    })
    model_group_id = api.register_model_group(
        model_group_name,
        description = "A model group for NLP models",
        cluster_url = cluster_url
    )
    current_model_id = api.get_model_group_model_id(
        model_group_id,
        cluster_url = cluster_url
    )
    client = OpenSearch(
        hosts=[cluster_url],
    )
    ml_client = MLCommonClient(client)
    if current_model_id is not None:
        current_model_info = ml_client.get_model_info(current_model_id)
        current_model_content_hash_value = current_model_info['model_content_hash_value']
        current_model_all_config = current_model_info['model_config']['all_config']
        with open(os.getenv('TORCHSCRIPT_MODEL_CONFIG_PATH'), 'r') as f:
            next_config = json.load(f)
        # Note: No exception handling. Therse fields must be present
        next_model_content_hash_value = next_config['model_content_hash_value']
        next_model_all_config = next_config['model_config']['all_config']
        if (
            current_model_content_hash_value == next_model_content_hash_value
            and current_model_all_config == next_model_all_config
        ):
            print("Current model and config have no changes")
            current_model_state = current_model_info['model_state']
            if current_model_state == 'DEPLOYED':
                # OK state. Exiting
                print("Model is deployed")
                return
            if current_model_state == 'DEPLOY_FAILED':
                # Attempt to execute deploy
                print("Redeploying a model")
                deploy_result = ml_client.deploy_model(
                    current_model_id,
                    wait_until_deployed = True
                )
                assert deploy_result['state'] == 'COMPLETED'
                return
            assert False, f"Unexpected model state {current_model_state}"


    model_id = ml_client.register_model(
        model_group_id = model_group_id,
        model_path = os.getenv('TORCHSCRIPT_MODEL_PATH'),
        model_config_path = os.getenv('TORCHSCRIPT_MODEL_CONFIG_PATH'),
        deploy_model = True,
        wait_until_deployed = True
    )
    print(model_id)
    current_model_info = ml_client.get_model_info(model_id)
    print(current_model_info)

def get_int_env_var(env_var_name, default_value):
    try:
        return int(os.getenv(env_var_name, default_value))
    except ValueError:
        return default_value

if __name__ == '__main__':
    sleep_on_failure = get_int_env_var('SLEEP_ON_FAILURE_SECONDS', 5)
    max_attempts = get_int_env_var('MAX_RETRY_ATTEMPTS', 1)

    attempts = 0
    while attempts < max_attempts:
        try:
            main()
            break  # If main() succeeds, exit the loop
        except:
            attempts += 1
            if attempts < max_attempts:
                is_to_sleep = sleep_on_failure > 0
                message = f"Attempt {attempts}/{max_attempts} failed."
                print(message + f" Sleeping for {sleep_on_failure} seconds." if is_to_sleep else message)
                if is_to_sleep:
                    time.sleep(sleep_on_failure)
            else:
                print(f"All {max_attempts} attempts failed. Raising the last error.")
                raise
