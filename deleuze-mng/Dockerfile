FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. 共通プロジェクトと対象プロジェクトの csproj をコピーして restore
COPY ["deleuze-shared/deleuze-shared.csproj", "deleuze-shared/"]
COPY ["deleuze-drive/deleuze-drive.csproj", "deleuze-drive/"]
RUN dotnet restore "deleuze-drive/deleuze-drive.csproj"

# 2. 全ソースコードをコピーしてビルド
COPY . .
WORKDIR "/src/deleuze-drive"
RUN dotnet build "deleuze-drive.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "deleuze-drive.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "deleuze-drive.dll"]