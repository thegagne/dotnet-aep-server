#!/usr/bin/env bash
#
# Deploy the AEP server to FLOCI as a real serverless stack: the Lambda container image behind an
# API Gateway HTTP API, backed by FLOCI's DynamoDB — the same topology as the AWS deploy, run
# entirely locally. FLOCI runs Lambda images via Docker, so it needs the host Docker socket
# (see run-floci.sh / the README).
#
#   ./run-floci.sh                 # start FLOCI with Docker access
#   ./deploy-lambda.sh             # build image, create Lambda + API Gateway, print the URL
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../../.." && pwd)"
ENDPOINT="${FLOCI_ENDPOINT:-http://localhost:4566}"
# How the Lambda container (spawned by FLOCI) reaches FLOCI's own services. FLOCI exposes itself
# to functions at this address; override if your setup differs.
INTERNAL_ENDPOINT="${FLOCI_INTERNAL_ENDPOINT:-http://floci:4566}"

export AWS_ACCESS_KEY_ID=local AWS_SECRET_ACCESS_KEY=local AWS_DEFAULT_REGION=us-east-1
awsf() { aws --endpoint-url "$ENDPOINT" "$@"; }

echo "==> Building the Lambda image (host Docker; FLOCI shares the socket)…"
docker build -f "$HERE/../Dockerfile" -t aep-lambda:latest "$ROOT"

echo "==> Creating the Lambda function from the image…"
awsf lambda delete-function --function-name aep >/dev/null 2>&1 || true
awsf lambda create-function \
  --function-name aep \
  --package-type Image \
  --code "ImageUri=aep-lambda:latest" \
  --role "arn:aws:iam::000000000000:role/aep-lambda" \
  --timeout 30 --memory-size 512 \
  --environment "Variables={Storage__Provider=dynamodb,Storage__DynamoDb__ServiceUrl=$INTERNAL_ENDPOINT,Storage__DynamoDb__CredentialsSource=Static,Storage__DynamoDb__AccessKey=local,Storage__DynamoDb__SecretKey=local,Storage__DynamoDb__TablePrefix=aep_}" \
  >/dev/null
awsf lambda wait function-active-v2 --function-name aep 2>/dev/null || sleep 5

echo "==> Wiring an API Gateway HTTP API (proxy) to the function…"
LAMBDA_ARN="arn:aws:lambda:us-east-1:000000000000:function:aep"
API_ID="$(awsf apigatewayv2 create-api \
  --name aep --protocol-type HTTP --target "$LAMBDA_ARN" \
  --query ApiId --output text)"
awsf lambda add-permission --function-name aep --statement-id apigw \
  --action lambda:InvokeFunction --principal apigateway.amazonaws.com >/dev/null 2>&1 || true

URL="$ENDPOINT/_apigw/$API_ID"  # FLOCI routes HTTP APIs under /_apigw/{apiId}; see its docs
echo
echo "Deployed. API id: $API_ID"
echo "Try it:"
echo "  curl $URL/openapi.json"
echo "  curl -X POST '$URL/publishers?id=acme' -H 'Content-Type: application/json' -d '{\"display_name\":\"Acme\"}'"
