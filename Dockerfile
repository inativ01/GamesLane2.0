FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/GamesLane.Server/GamesLane.Server.csproj src/GamesLane.Server/
RUN dotnet restore src/GamesLane.Server/GamesLane.Server.csproj
COPY src/GamesLane.Server/ src/GamesLane.Server/
RUN dotnet publish src/GamesLane.Server/GamesLane.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GamesLane.Server.dll"]
