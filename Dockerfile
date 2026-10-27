# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["nuget.config", "./"]
COPY ["src/DocumentOps.Core/DocumentOps.Core.csproj", "src/DocumentOps.Core/"]
COPY ["src/DocumentOps.Application/DocumentOps.Application.csproj", "src/DocumentOps.Application/"]
COPY ["src/DocumentOps.Infrastructure/DocumentOps.Infrastructure.csproj", "src/DocumentOps.Infrastructure/"]
COPY ["src/DocumentOps.Api/DocumentOps.Api.csproj", "src/DocumentOps.Api/"]
COPY ["tests/DocumentOps.Tests/DocumentOps.Tests.csproj", "tests/DocumentOps.Tests/"]

RUN dotnet restore "src/DocumentOps.Api/DocumentOps.Api.csproj"

COPY . .
WORKDIR "/src/src/DocumentOps.Api"
RUN dotnet build "DocumentOps.Api.csproj" -c Release -o /app/build

# Test Stage
WORKDIR "/src/tests/DocumentOps.Tests"
RUN dotnet test "DocumentOps.Tests.csproj" -c Release --no-restore

# Publish Stage
WORKDIR "/src/src/DocumentOps.Api"
RUN dotnet publish "DocumentOps.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5080
ENV ASPNETCORE_URLS=http://+:5080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DocumentOps.Api.dll"]
