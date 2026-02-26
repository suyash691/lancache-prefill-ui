# Stage 1: Build frontend
FROM node:20-alpine AS frontend
WORKDIR /frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/LancachePrefill/LancachePrefill.csproj src/LancachePrefill/
RUN dotnet restore src/LancachePrefill/LancachePrefill.csproj
COPY src/ src/
# Copy frontend build output into wwwroot
COPY --from=frontend /src/LancachePrefill/wwwroot src/LancachePrefill/wwwroot/
RUN dotnet publish src/LancachePrefill/LancachePrefill.csproj -c Release -o /app

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 28542
ENTRYPOINT ["dotnet", "LancachePrefill.dll"]