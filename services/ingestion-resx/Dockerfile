FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY resx_parser.py graph_builder.py embedder.py seeder.py schema.sql ./

CMD ["python", "seeder.py"]
