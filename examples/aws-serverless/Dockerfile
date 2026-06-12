# syntax=docker/dockerfile:1
#
# AEP server packaged as an AWS Lambda container: a DynamoDB-only image (no SQLite native
# binding) plus the AWS Lambda Web Adapter, which bridges Lambda invocations to the unmodified
# ASP.NET app on port 8080. The same image runs as a plain HTTP container (the adapter is inert
# outside the Lambda runtime), which is how docker-compose.yml runs it locally against FLOCI.
#
# Build from the REPO ROOT:
#   docker build -f examples/aws-serverless/Dockerfile -t aep-lambda .

# ---- build (DynamoDB-only) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Aep.Server/Aep.Server.csproj -c Release -o /app \
      -p:IncludeSqlite=false -p:IncludeDynamoDb=true

# ---- runtime (aspnet + Lambda Web Adapter) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Drop-in Lambda extension: no application code changes needed.
COPY --from=public.ecr.aws/awsguru/aws-lambda-adapter:0.9.0 /lambda-adapter /opt/extensions/lambda-adapter

COPY --from=build /app ./
# This example ships its own resources.yaml (overrides the baked-in sample).
COPY examples/aws-serverless/resources.yaml ./resources.yaml

ENV Storage__Provider=dynamodb \
    # On AWS, credentials come from the function's execution role (Ambient). For local FLOCI,
    # docker-compose overrides ServiceUrl + Static creds.
    Storage__DynamoDb__CredentialsSource=Ambient \
    Storage__DynamoDb__TablePrefix=aep_ \
    ASPNETCORE_HTTP_PORTS=8080 \
    AWS_LWA_PORT=8080

EXPOSE 8080
ENTRYPOINT ["dotnet", "Aep.Server.dll"]
