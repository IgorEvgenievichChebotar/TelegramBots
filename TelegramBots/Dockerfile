FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["TelegramBots/TelegramBots.csproj", "TelegramBots/"]
RUN dotnet restore "TelegramBots/TelegramBots.csproj"
COPY . .
WORKDIR "/src/TelegramBots"
RUN dotnet build "TelegramBots.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TelegramBots.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramBots.dll"]
