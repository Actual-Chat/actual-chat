# OpenSearch requires pandas v2.0.3.
# So we stick with python 3.9 because it is the only python version
# having prebuilt wheel (*.whl) file for pandas 2.0.3,
# as prebuilt *.whl drastically reduces package installation time.
# See https://www.piwheels.org/project/pandas/ for possible python/pandas combos.

FROM python:3.9
RUN pip install pandas==2.0.3 deprecated opensearch-py opensearch-py-ml requests
WORKDIR /workdir
CMD python ./opensearch-setup.py
