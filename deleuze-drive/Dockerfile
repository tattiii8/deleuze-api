FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. csproj を先にコピーして依存関係を restore
COPY ["deleuze-drive/deleuze-drive.csproj", "deleuze-drive/"]
COPY ["deleuze-shared/deleuze-shared.csproj", "deleuze-shared/"]
RUN dotnet restore "deleuze-drive/deleuze-drive.csproj"

# 2. 両方のソースコードをコピー
COPY deleuze-shared/ deleuze-shared/
COPY deleuze-drive/ deleuze-drive/

# 3. ビルド実行
WORKDIR "/src/deleuze-drive"
RUN dotnet build "deleuze-drive.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "deleuze-drive.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# build ステージ上のパスから直にコピー
COPY --from=build /src/deleuze-drive/DbMigration ./DbMigration
ENTRYPOINT ["dotnet", "deleuze-drive.dll"]