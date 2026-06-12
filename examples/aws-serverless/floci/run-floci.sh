#!/usr/bin/env bash
# Start FLOCI with access to the host Docker socket, so it can run Lambda container images.
set -euo pipefail

SOCK="${DOCKER_SOCK:-$(docker context inspect --format '{{.Endpoints.docker.Host}}' 2>/dev/null | sed 's|unix://||')}"
SOCK="${SOCK:-/var/run/docker.sock}"

docker rm -f floci >/dev/null 2>&1 || true
docker run -d --name floci -p 4566:4566 -v "$SOCK:/var/run/docker.sock" hectorvent/floci:latest >/dev/null
echo "FLOCI up at http://localhost:4566 (docker socket: $SOCK)"
