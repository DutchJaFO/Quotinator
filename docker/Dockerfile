# Stage 1 — build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer — selective copy of .csproj files and SDK config so NuGet packages
# are layer-cached independently of source changes. Add a line here for each new project.
COPY Directory.Build.props .
COPY src/Quotinator.Constants/Quotinator.Constants.csproj src/Quotinator.Constants/
COPY src/Quotinator.Changelog/Quotinator.Changelog.csproj src/Quotinator.Changelog/
COPY src/Quotinator.Data/Quotinator.Data.csproj src/Quotinator.Data/
COPY src/Quotinator.Core/Quotinator.Core.csproj src/Quotinator.Core/
COPY src/Quotinator.Engine/Quotinator.Engine.csproj src/Quotinator.Engine/
COPY src/Quotinator.Converters.Vilaboim/Quotinator.Converters.Vilaboim.csproj src/Quotinator.Converters.Vilaboim/
COPY src/Quotinator.Converters.NikhilNamal17/Quotinator.Converters.NikhilNamal17.csproj src/Quotinator.Converters.NikhilNamal17/
COPY src/Quotinator.Api/Quotinator.Api.csproj src/Quotinator.Api/
RUN dotnet restore src/Quotinator.Api/Quotinator.Api.csproj

# Build layer — copy everything not excluded by .dockerignore so no file is ever
# accidentally omitted when new projects or root-level SDK files are added.
COPY . .
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

# data/sources/ is included in the publish output (CopyToPublishDirectory=PreserveNewest).
# Mount a volume at /app/data for the SQLite database and user import files.
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "Quotinator.Api.dll"]
