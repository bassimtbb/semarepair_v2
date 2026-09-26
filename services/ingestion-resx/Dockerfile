FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY resx_parser.py graph_builder.py embedder.py seeder.py schema.sql ./

# Extension v2 (docs/Architecture_Extension_v2.md). Listed separately from
# the line above so it stays obvious which files drive the working product
# and which belong to the extension - the CMD still runs seeder.py, and
# technical_seeder.py is only ever invoked explicitly.
COPY technical_parser.py technical_seeder.py technical_embedder.py schema_technical.sql ./

CMD ["python", "seeder.py"]
