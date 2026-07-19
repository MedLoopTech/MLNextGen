FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MedLoop.NextGen.csproj", "./"]
RUN dotnet restore "./MedLoop.NextGen.csproj"
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN adduser --disabled-password --gecos "" appuser
COPY --from=build /app/publish .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MedLoop.NextGen.dll"]
