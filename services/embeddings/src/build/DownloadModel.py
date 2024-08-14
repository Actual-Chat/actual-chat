import os
import sys
from transformers import AutoTokenizer, AutoModel

model = sys.argv[1]

data_path = os.path.join('./data', model)

# get tokenizer
tokenizer = AutoTokenizer.from_pretrained(model)

# get model
model = AutoModel.from_pretrained(model, trust_remote_code=True)

# save tokenizer
tokenizer.save_pretrained(os.path.join(data_path, 'tokenizer'))

# save model
model.save_pretrained(os.path.join(data_path, 'model'))
