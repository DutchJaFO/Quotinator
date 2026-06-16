# Stage 1 — build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (layer-cached separately from source copy)
COPY src/Quotinator.Constants/Quotinator.Constants.csproj src/Quotinator.Constants/
COPY src/Quotinator.Data/Quotinator.Data.csproj src/Quotinator.Data/
COPY src/Quotinator.Core/Quotinator.Core.csproj src/Quotinator.Core/
COPY src/Quotinator.Api/Quotinator.Api.csproj src/Quotinator.Api/
RUN dotnet restore src/Quotinator.Api/Quotinator.Api.csproj

# Build and publish
COPY src/Quotinator.Constants/ src/Quotinator.Constants/
COPY src/Quotinator.Data/ src/Quotinator.Data/
COPY src/Quotinator.Core/ src/Quotinator.Core/
COPY src/Quotinator.Api/ src/Quotinator.Api/
COPY data/ data/
COPY CHANGELOG.md .
# Note: --no-restore is intentionally absent. The Blazor static web assets pipeline
# (which produces _framework/blazor.web.js) needs .razor files present when the
# assets manifest is generated. The restore-only layer above runs without source files,
# so the manifest is incomplete; omitting --no-restore lets publish regenerate it
# correctly with the full source. NuGet packages are still layer-cached from above.
RUN dotnet publish src/Quotinator.Api/Quotinator.Api.csproj \
    --configuration Release \
    --output /app/publish

# Stage 2 — runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 8080 — direct access (HTTP or HTTPS); 8099 — Home Assistant ingress (always HTTP)
EXPOSE 8080 8099

# Clear the base-image default (ASPNETCORE_HTTP_PORTS=8080) so port binding is
# controlled entirely by the application's Kestrel configuration in Program.cs.
# For HA add-on deployments, addon/config.yaml overrides this with 8099 for ingress.
ENV ASPNETCORE_HTTP_PORTS=""

COPY --from=build /app/publish .

# data/quotes.json is included in the publish output (CopyToPublishDirectory=PreserveNewest).
# Mount a volume at /app/data to override with a custom dataset at runtime.
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "Quotinator.Api.dll"]
