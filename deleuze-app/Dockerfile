FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. csproj のみを先に COPY して restore (キャッシュ効率化)
COPY ["deleuze-app/deleuze-app.csproj", "deleuze-app/"]
COPY ["deleuze-shared/deleuze-shared.csproj", "deleuze-shared/"]
RUN dotnet restore "deleuze-app/deleuze-app.csproj"

# 2. ソースコード全体を COPY して publish
COPY deleuze-app/ deleuze-app/
COPY deleuze-shared/ deleuze-shared/
WORKDIR /src/deleuze-app
RUN dotnet publish "deleuze-app.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "deleuze-app.dll"]