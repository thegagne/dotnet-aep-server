# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Storage backends are opt-in at build time (see README "Storage backends"). Override per image:
#   docker build --build-arg IncludePostgres=true --build-arg IncludeSqlite=false ...
ARG IncludeSqlite=true
ARG IncludePostgres=false
ARG IncludeDynamoDb=false

# Directory.Build.props sets TargetFramework etc. for every project, so it must come along.
COPY Directory.Build.props ./
COPY src/ src/

RUN dotnet restore src/Aep.Server/Aep.Server.csproj \
      -p:IncludeSqlite=$IncludeSqlite -p:IncludePostgres=$IncludePostgres -p:IncludeDynamoDb=$IncludeDynamoDb
RUN dotnet publish src/Aep.Server/Aep.Server.csproj -c Release -o /app --no-restore \
      -p:IncludeSqlite=$IncludeSqlite -p:IncludePostgres=$IncludePostgres -p:IncludeDynamoDb=$IncludeDynamoDb

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The published output already contains resources.yaml (baked in at build time).
COPY --from=build /app ./

# SQLite data directory, writable by the image's non-root user.
RUN mkdir -p /data && chown $APP_UID /data
VOLUME ["/data"]

ENV Storage__Provider=sqlite \
    Storage__Sqlite__ConnectionString="Data Source=/data/aep.db" \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080
ENTRYPOINT ["dotnet", "Aep.Server.dll"]
