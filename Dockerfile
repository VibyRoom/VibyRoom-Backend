FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

COPY src/ ./src/

RUN dotnet restore \
    src/Api/VibyRoom.Api/VibyRoom.Api.csproj

RUN dotnet publish \
    src/Api/VibyRoom.Api/VibyRoom.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /publish \
    /p:UseAppHost=false

RUN dotnet tool install \
    --tool-path /tools \
    dotnet-ef \
    --version 10.0.13

RUN mkdir -p /tmp/vibyroom-keys

RUN ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeysDirectory=/tmp/vibyroom-keys \
    Realtime__AllowedOrigins__0=https://localhost \
    ConnectionStrings__DefaultConnection="Host=localhost;Database=vibyroom;Username=vibyroom;Password=placeholder" \
    /tools/dotnet-ef migrations bundle \
    --project src/Infrastructure/VibyRoom.Infrastructure.Persistence \
    --startup-project src/Api/VibyRoom.Api \
    --context VibyRoomDbContext \
    --configuration Release \
    --output /efbundle \
    --force

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /publish/ ./
COPY --from=build /efbundle ./efbundle

RUN mkdir -p /app/keys \
    && chown -R $APP_UID:$APP_UID /app/keys

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "VibyRoom.Api.dll"]