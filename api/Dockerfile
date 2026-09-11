# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY api/CheckPoint.Api/CheckPoint.Api.csproj api/CheckPoint.Api/
RUN dotnet restore api/CheckPoint.Api/CheckPoint.Api.csproj

COPY api/CheckPoint.Api/ api/CheckPoint.Api/
RUN dotnet publish api/CheckPoint.Api/CheckPoint.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CheckPoint.Api.dll"]
