#!/usr/bin/env bash
set -euo pipefail

# Starts (or creates) the local Docling container used by CustomPdfDecoder.
# Configurable via env vars:
#   DOCLING_CONTAINER_NAME - Docker container name (default: docling-serve)
#   DOCLING_IMAGE          - Image reference (default: quay.io/docling-project/docling-serve:v1.8.0)
#   DOCLING_PORT           - Host port to expose Docling API/UI on (default: 5001)
#   DOCLING_ARTIFACTS_DIR  - Host directory for cached models (default: $HOME/.cache/docling/models)

CONTAINER_NAME=${DOCLING_CONTAINER_NAME:-docling-serve}
IMAGE=${DOCLING_IMAGE:-quay.io/docling-project/docling-serve:v1.8.0}
PORT=${DOCLING_PORT:-5001}
ARTIFACTS_DIR=${DOCLING_ARTIFACTS_DIR:-"$HOME/.cache/docling/models"}
CONTAINER_ARTIFACTS_PATH=/opt/app-root/src/.cache/docling/models

mkdir -p "$ARTIFACTS_DIR"

if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
  if docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "Docling container '${CONTAINER_NAME}' is already running on port ${PORT}."
    exit 0
  fi
  echo "Starting existing Docling container '${CONTAINER_NAME}'..."
  docker start "$CONTAINER_NAME"
  exit 0
fi

echo "Pulling ${IMAGE} (if needed)..."
docker pull "$IMAGE"

echo "Launching Docling container '${CONTAINER_NAME}' on port ${PORT}..."
docker run -d \
  --name "$CONTAINER_NAME" \
  -p "${PORT}:5001" \
  -e DOCLING_SERVE_ENABLE_UI=true \
  -e DOCLING_SERVE_ARTIFACTS_PATH="$CONTAINER_ARTIFACTS_PATH" \
  -v "$ARTIFACTS_DIR":"$CONTAINER_ARTIFACTS_PATH" \
  "$IMAGE" \
  docling-serve run

echo "Docling is up → http://localhost:${PORT}"
