FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# csproj をルート基準でコピーして restore
COPY ["deleuze-app/deleuze-app.csproj", "deleuze-app/"]
COPY ["deleuze-shared/deleuze-shared.csproj", "deleuze-shared/"]
RUN dotnet restore "deleuze-app/deleuze-app.csproj"

# ソースコードをコピー
COPY deleuze-shared/ deleuze-shared/
COPY deleuze-app/ deleuze-app/

WORKDIR "/src/deleuze-app"
RUN dotnet build "deleuze-app.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "deleuze-app.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "deleuze-app.dll"]