# WillowWallet Server — docker build -f server/Dockerfile -t willowwallet-server .   (from the repository root)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/WillowWallet.Sync/WillowWallet.Sync.csproj server/WillowWallet.Sync/
COPY server/WillowWallet.Server/WillowWallet.Server.csproj server/WillowWallet.Server/
RUN dotnet restore server/WillowWallet.Server/WillowWallet.Server.csproj
COPY server/ server/
COPY src/CoinPilot.Desktop/Assets/coinpilot.ico src/CoinPilot.Desktop/Assets/
RUN dotnet publish server/WillowWallet.Server/WillowWallet.Server.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
LABEL org.opencontainers.image.title="WillowWallet Server" \
      org.opencontainers.image.description="Self-hosted, end-to-end encrypted sync server for WillowWallet, with a web panel." \
      org.opencontainers.image.vendor="Bytehawk Studio" \
      org.opencontainers.image.source="https://github.com/adamglassner/CoinPilot"
WORKDIR /app
COPY --from=build /app .
ENV WW_DATA=/data WW_PORT=8484
VOLUME /data
EXPOSE 8484
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s CMD ["dotnet", "WillowWalletServer.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "WillowWalletServer.dll"]
