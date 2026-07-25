# syntax=docker/dockerfile:1

FROM node:24-alpine AS web-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY backend/src/Echora.Api/Echora.Api.csproj backend/src/Echora.Api/
RUN dotnet restore backend/src/Echora.Api/Echora.Api.csproj
COPY backend/src/Echora.Api/ backend/src/Echora.Api/
RUN dotnet publish backend/src/Echora.Api/Echora.Api.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__LocalPath=/app/attachments
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=api-build /out ./
COPY --from=web-build /src/web/dist ./wwwroot
RUN mkdir -p /app/attachments && chown -R "$APP_UID:$APP_UID" /app
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Echora.Api.dll"]
