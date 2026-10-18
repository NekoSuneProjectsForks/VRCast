FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY VRCast.SessionServer.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=build /publish ./
USER $APP_UID
ENV VRCAST_SESSION_BIND=0.0.0.0 VRCAST_SESSION_PORT=39580
EXPOSE 39580
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s CMD wget -q -O /dev/null http://127.0.0.1:39580/healthz || exit 1
ENTRYPOINT ["dotnet", "VRCast.SessionServer.dll"]
