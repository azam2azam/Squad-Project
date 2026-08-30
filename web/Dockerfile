# Build from the repository root:
#   docker build -f web/Dockerfile -t squad-status-board-web .
#
# NOTE: not verified locally — Docker is not installed on the development machine.

FROM node:22-alpine AS build
WORKDIR /src

# package files first so npm ci is cached until dependencies change.
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime

COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/web/browser /usr/share/nginx/html

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -qO- http://localhost:8080/ >/dev/null || exit 1
