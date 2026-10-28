FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GamesLane.Server/GamesLane.Server.csproj GamesLane.Server/
RUN dotnet restore GamesLane.Server/GamesLane.Server.csproj
COPY GamesLane.Server/ GamesLane.Server/
RUN dotnet publish GamesLane.Server/GamesLane.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GamesLane.Server.dll"]
