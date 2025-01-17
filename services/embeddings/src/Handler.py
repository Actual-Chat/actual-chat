import torch
import torch.nn.functional as F

import logging
import transformers
import os

from ts.torch_handler.base_handler import BaseHandler
from transformers import AutoModel, AutoTokenizer

logger = logging.getLogger(__name__)
logger.info("Transformers version %s", transformers.__version__)

#Mean Pooling - Take attention mask into account for correct averaging
def mean_pooling(model_output, attention_mask):
    token_embeddings = model_output[0] #First element of model_output contains all token embeddings
    input_mask_expanded = attention_mask.unsqueeze(-1).expand(token_embeddings.size()).float()
    return torch.sum(token_embeddings * input_mask_expanded, 1) / torch.clamp(input_mask_expanded.sum(1), min=1e-9)


class ModelHandler(BaseHandler):
    def initialize(self, context):
        """
        Initialize function loads the model and the tokenizer

        Args:
            context (context): It is a JSON Object containing information
            pertaining to the model artifacts parameters.

        Raises:
            RuntimeError: Raises the Runtime error when the model or
            tokenizer is missing
        """

        properties = context.system_properties
        self.manifest = context.manifest
        model_dir = properties.get("model_dir")

        # use GPU if available
        self.device = torch.device(
            "cuda:" + str(properties.get("gpu_id"))
            if torch.cuda.is_available() and properties.get("gpu_id") is not None
            else "cpu"
        )
        logger.info(f'Using device {self.device}')

        # load the model
        model_file = self.manifest['model']['modelFile']
        model_path = os.path.join(model_dir, model_file)

        if os.path.isfile(model_path):
            self.model = AutoModel.from_pretrained(model_dir, trust_remote_code=True)
            self.model.to(self.device)
            self.model.eval()
            logger.info(f'Successfully loaded model from {model_file}')
        else:
            raise RuntimeError('Missing the model file')

        # load tokenizer
        self.tokenizer = AutoTokenizer.from_pretrained(model_dir)
        if self.tokenizer is not None:
            logger.info('Successfully loaded tokenizer')
        else:
            raise RuntimeError('Missing tokenizer')

        self.initialized = True

    def preprocess_text(self, texts):
        logger.info(f'Received {len(texts)} texts. Begin tokenizing')

        # tokenize the texts
        tokenized_data = self.tokenizer(texts,
                                        padding=True,
                                        truncation=True,
                                        return_tensors='pt')

        logger.info('Tokenization process completed')
        return tokenized_data

    def preprocess(self, requests):
        """
        Tokenize the input text using the suitable tokenizer and convert
        it to tensor

        If token_ids is provided, the json must be of the form
        {'input_ids': [[101, 102]], 'token_type_ids': [[0, 0]], 'attention_mask': [[1, 1]]}

        Args:
            requests: A list containing a dictionary, might be in the form
            of [{'body': json_file}] or [{'data': json_file}] or [{'token_ids': json_file}]
        Returns:
            the tensor containing the batch of token vectors.
        """

        # unpack the data
        data = requests[0].get('body')
        if data is None:
            data = requests[0].get('data')

        texts = data.get('input')
        if texts is not None:
            logger.info('Text provided')
            return self.preprocess_text(texts)

        encodings = data.get('encodings')
        if encodings is not None:
            logger.info('Encodings provided')
            return transformers.BatchEncoding(data={k: torch.tensor(v) for k, v in encodings.items()})

        raise Exception("unsupported payload")

    def inference(self, inputs):
        """
        Compute the embeddings given the batch of tokens.

        Args:
            inputs: encoded data
        Returns:
            the tensor containing the batch embeddings.
        """
        print(inputs)

        with torch.no_grad():
            model_output = self.model(**inputs.to(self.device))
        sentence_embeddings = mean_pooling(model_output, inputs['attention_mask'])
        sentence_embeddings = F.normalize(sentence_embeddings, p=2, dim=1)
        logger.info('Embeddings successfully computed')
        return sentence_embeddings

    def postprocess(self, outputs: list):
        """
        Convert the tensor into a list.

        Args:
            outputs: tensor containing the embeddings.
        Returns:
            the list of list of floating point representing the embeddings for the batch.
        """
        logger.info('Postprocessing successfully computed')
        return [outputs.tolist()]
