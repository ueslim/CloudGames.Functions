# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything (excluding obj/bin via .dockerignore would be ideal)
COPY . ./

# Restore and publish in one step (avoids Windows-specific obj cache issues)
RUN dotnet publish CloudGames.Functions/CloudGames.Functions.csproj -c Release -o /app/publish

# Runtime stage (Azure Functions .NET Isolated)
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0

# Azure Functions expects the app under /home/site/wwwroot
WORKDIR /home/site/wwwroot
COPY --from=build /app/publish .

# Recommended envs
ENV AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    DOTNET_RUNNING_IN_CONTAINER=true

# Expose default Functions port
EXPOSE 80

# ENTRYPOINT provided by base image
