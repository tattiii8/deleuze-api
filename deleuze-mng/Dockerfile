FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["deleuze-mng/deleuze-mng.csproj", "deleuze-mng/"]
COPY ["deleuze-shared/deleuze-shared.csproj", "deleuze-shared/"]
RUN dotnet restore "deleuze-mng/deleuze-mng.csproj"

COPY deleuze-shared/ deleuze-shared/
COPY deleuze-mng/ deleuze-mng/

WORKDIR "/src/deleuze-mng"
RUN dotnet build "deleuze-mng.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "deleuze-mng.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "deleuze-mng.dll"]