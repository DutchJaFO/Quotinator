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
RUN dotnet publish src/Quotinator.Api/Quotinator.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Stage 2 — runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 8080 — direct access; 8099 — Home Assistant ingress
EXPOSE 8080 8099

# Mount quotes.json here at runtime
VOLUME ["/app/data"]

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Quotinator.Api.dll"]
