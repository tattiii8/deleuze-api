FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["deleuze-app.csproj", "."]
RUN dotnet restore "deleuze-app.csproj"
COPY . .
RUN dotnet publish "deleuze-app.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "deleuze-app.dll"]