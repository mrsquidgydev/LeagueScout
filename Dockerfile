# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so package downloads are cached between source changes.
COPY LeagueScout.slnx ./
COPY src/LeagueScout.Domain/LeagueScout.Domain.csproj src/LeagueScout.Domain/
COPY src/LeagueScout.Application/LeagueScout.Application.csproj src/LeagueScout.Application/
COPY src/LeagueScout.Infrastructure/LeagueScout.Infrastructure.csproj src/LeagueScout.Infrastructure/
COPY src/LeagueScout.Bot/LeagueScout.Bot.csproj src/LeagueScout.Bot/
RUN dotnet restore src/LeagueScout.Bot/LeagueScout.Bot.csproj

COPY src/ src/
RUN dotnet publish src/LeagueScout.Bot/LeagueScout.Bot.csproj -c Release -o /app --no-restore

# Optional: docker build --target test .
FROM build AS test
COPY tests/ tests/
RUN dotnet test tests/LeagueScout.Tests/LeagueScout.Tests.csproj -c Release

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# SQLite database lives on a mounted volume.
RUN mkdir -p /data && chown app:app /data
VOLUME /data

ENV DATABASE_PATH=/data/leaguescout.db \
    DOTNET_ENVIRONMENT=Production

COPY --from=build /app .
USER app

# Migrates the database, connects to Discord and starts the sync worker.
ENTRYPOINT ["dotnet", "LeagueScout.Bot.dll"]
