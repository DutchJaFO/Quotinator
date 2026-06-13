# Stage 1 — build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (layer-cached separately from source copy)
COPY src/Quotinator.Core/Quotinator.Core.csproj src/Quotinator.Core/
COPY src/Quotinator.Api/Quotinator.Api.csproj src/Quotinator.Api/
RUN dotnet restore src/Quotinator.Api/Quotinator.Api.csproj

# Build and publish
COPY src/Quotinator.Core/ src/Quotinator.Core/
COPY src/Quotinator.Api/ src/Quotinator.Api/
COPY data/ data/
RUN dotnet publish src/Quotinator.Api/Quotinator.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Stage 2 — runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 8080 — direct access; 8099 — Home Assistant ingress
EXPOSE 8080 8099

COPY --from=build /app/publish .

# data/quotes.json is included in the publish output (CopyToPublishDirectory=PreserveNewest).
# Mount a volume at /app/data to override with a custom dataset at runtime.
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "Quotinator.Api.dll"]
