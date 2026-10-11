FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Clary.Api/Clary.Api.csproj backend/Clary.Api/
RUN dotnet restore backend/Clary.Api/Clary.Api.csproj
COPY backend/Clary.Api/ backend/Clary.Api/
COPY database/ database/
COPY samples/ samples/
RUN dotnet publish backend/Clary.Api/Clary.Api.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/uploads && chown app:app /app/uploads
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Clary.Api.dll"]
