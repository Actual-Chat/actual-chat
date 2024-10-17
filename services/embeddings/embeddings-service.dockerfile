ARG MODEL_NAME="Alibaba-NLP/gte-multilingual-base"

# Builder image
FROM pytorch/torchserve AS builder

ARG MODEL_NAME

RUN pip install transformers

WORKDIR /usr/app

COPY ./src/build ./scripts

RUN python ./scripts/DownloadModel.py ${MODEL_NAME}

RUN --mount=type=bind,source=./src/Handler.py,target=./Handler.py \
    bash ./scripts/create-model-archive.sh ${MODEL_NAME} ./Handler.py

# Production image
FROM pytorch/torchserve

RUN pip install transformers

ADD ./src/run/start-torchserve.sh start-torchserve.sh

COPY --from=builder /usr/app/model_store model_store

CMD ["./start-torchserve.sh"]
