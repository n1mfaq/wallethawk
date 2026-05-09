# syntax=docker/dockerfile:1.6
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY WalletHawk.slnx ./
COPY src/WalletHawk.Domain/*.csproj src/WalletHawk.Domain/
COPY src/WalletHawk.Data/*.csproj src/WalletHawk.Data/
COPY src/WalletHawk.Infrastructure/*.csproj src/WalletHawk.Infrastructure/
COPY src/WalletHawk.Bot/*.csproj src/WalletHawk.Bot/
COPY src/WalletHawk.Worker/*.csproj src/WalletHawk.Worker/

RUN dotnet restore src/WalletHawk.Worker/WalletHawk.Worker.csproj

COPY src/. src/
RUN dotnet publish src/WalletHawk.Worker/WalletHawk.Worker.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "WalletHawk.Worker.dll"]
