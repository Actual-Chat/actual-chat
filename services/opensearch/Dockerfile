# OpenSearch requires pandas v2.0.3.
# So we stick with python 3.9 because it is the only python version
# having prebuilt wheel (*.whl) file for pandas 2.0.3,
# as prebuilt *.whl drastically reduces package installation time.
# See https://www.piwheels.org/project/pandas/ for possible python/pandas combos.

FROM curlimages/curl:latest AS downloader
ARG MODEL_FILE="sentence-transformers_paraphrase-MiniLM-L3-v2-1.0.1-torch_script.zip"
RUN curl -Lf "https://artifacts.opensearch.org/models/ml-models/huggingface/sentence-transformers/paraphrase-MiniLM-L3-v2/1.0.1/torch_script/${MODEL_FILE}" \
    -o /tmp/${MODEL_FILE}


FROM python:3.9.19-slim-bookworm
ENV TORCHSCRIPT_MODEL_CONFIG_PATH='/ml_model/config.json' \
    OPENSEARCH_CLUSTER_URL='http://opensearch-node:9200' \
    OPENSEARCH_CLIENT_CERT_PATH='' \
    OPENSEARCH_CLIENT_KEY_PATH='' \
    OPENSEARCH_CA_CERT_PATH='' \
    OPENSEARCH_ML_MODEL_GROUP='NLP_model_group' \
    SLEEP_ON_FAILURE_SECONDS=15 \
    MAX_RETRY_ATTEMPTS=3 \
    TORCHSCRIPT_MODEL_PATH='/ml_model/${MODEL_FILE}' \
    TORCHSCRIPT_MODEL_CONFIG_PATH='/ml_model/config.json'
RUN pip install pandas==2.0.3 deprecated opensearch-py opensearch-py-ml requests
WORKDIR /workdir
COPY ml_model/ /ml_model/
COPY --from=downloader /tmp/${MODEL_FILE} /ml_model/${MODEL_FILE}
COPY opensearch-setup.py .

CMD python ./opensearch-setup.py
