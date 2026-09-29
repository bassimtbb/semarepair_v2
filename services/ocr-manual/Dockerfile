FROM python:3.11-slim

# tesseract-ocr-ita is the Italian language pack. Without it Tesseract falls
# back to English and mangles every accented word and every "�" it meets -
# the manual is entirely in Italian.
RUN apt-get update \
 && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-ita \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
RUN pip install --no-cache-dir pillow pytesseract

COPY ocr.py .
CMD ["python", "ocr.py"]
