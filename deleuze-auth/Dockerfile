FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG TARGETARCH
WORKDIR /src

COPY ["deleuze-auth.csproj", "./"]
RUN dotnet restore -a $TARGETARCH

COPY . .
RUN dotnet publish "deleuze-auth.csproj" -c Release -o /app/publish -a $TARGETARCH --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "deleuze-auth.dll"]