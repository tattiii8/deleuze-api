FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG TARGETARCH
WORKDIR /src

# 1. csproj を先にコピーして依存関係を restore
COPY ["deleuze-auth/deleuze-auth.csproj", "deleuze-auth/"]
COPY ["deleuze-shared/deleuze-shared.csproj", "deleuze-shared/"]
RUN dotnet restore "deleuze-auth/deleuze-auth.csproj" -a $TARGETARCH

# 2. ソースコード全体をコピー (パスの末尾のスラッシュを外して正確な位置に配置)
COPY deleuze-shared ./deleuze-shared
COPY deleuze-auth ./deleuze-auth

# 3. ビルド＆パブリッシュ実行
WORKDIR "/src/deleuze-auth"
RUN dotnet publish "deleuze-auth.csproj" -c Release -o /app/publish -a $TARGETARCH --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "deleuze-auth.dll"]