# Build from the repository root:
#   docker build -f src/Api/Dockerfile -t squad-status-board-api .
#
# NOTE: not verified locally — Docker is not installed on the development machine.
# See README "Known environment constraints".

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only project files first so restore is cached until dependencies change.
COPY global.json Directory.Build.props ./
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Application/Application.csproj src/Application/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Chromium for server-side PNG/PDF export. Installed here rather than downloaded at
# runtime by PuppeteerSharp, so the image is self-contained and the first export is
# not a several-minute download. Set Export__ChromiumPath to the path below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        chromium fonts-liberation ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

ENV Export__ChromiumPath=/usr/bin/chromium \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Runs unprivileged. Chromium is launched with --no-sandbox by the renderer, which is
# acceptable only because it renders our own trusted slide route and nothing else.
RUN useradd --uid 64198 --create-home appuser
USER 64198

COPY --from=build /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
