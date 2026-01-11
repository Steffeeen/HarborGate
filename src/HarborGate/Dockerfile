# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["src/HarborGate/HarborGate.csproj", "src/HarborGate/"]
RUN dotnet restore "src/HarborGate/HarborGate.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/src/HarborGate"
RUN dotnet build "HarborGate.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "HarborGate.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install ca-certificates, wget, and curl for health checks and Pebble root CA installation
RUN apt-get update && apt-get install -y \
    ca-certificates \
    wget \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Expose ports
EXPOSE 80
EXPOSE 443

# Copy published app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV HARBORGATE_DOCKER_SOCKET=/var/run/docker.sock

ENTRYPOINT ["dotnet", "HarborGate.dll"]
