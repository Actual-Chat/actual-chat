# OpenSearch requires pandas v2.0.3.
# So we stick with python 3.9 because it is the only python version
# having prebuilt wheel (*.whl) file for pandas 2.0.3,
# as prebuilt *.whl drastically reduces package installation time.
# See https://www.piwheels.org/project/pandas/ for possible python/pandas combos.

FROM python:3.9.19-slim-bookworm
ENV TORCHSCRIPT_MODEL_CONFIG_PATH='/ml_model/config.json' \
    OPENSEARCH_CLUSTER_URL='http://opensearch-node:9200' \
    OPENSEARCH_CLIENT_CERT_PATH='' \
    OPENSEARCH_CLIENT_KEY_PATH='' \
    OPENSEARCH_CA_CERT_PATH='' \
    OPENSEARCH_ML_MODEL_GROUP='NLP_model_group' \
    SLEEP_ON_FAILURE_SECONDS=15 \
    MAX_RETRY_ATTEMPTS=3 \
    TORCHSCRIPT_MODEL_PATH='/ml_model/sentence-transformers_paraphrase-MiniLM-L3-v2-1.0.1-torch_script.zip' \
    TORCHSCRIPT_MODEL_CONFIG_PATH='/ml_model/config.json'
RUN pip install pandas==2.0.3 deprecated opensearch-py opensearch-py-ml requests
WORKDIR /workdir
COPY ml_model/ /ml_model/
COPY opensearch-setup.py .

CMD python ./opensearch-setup.py
