# See https://aka.ms/customizecontainer for more info.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# (Opcional) Para no ser root (pero hay que crear 'app' antes de usar 'USER app'):
# RUN useradd -m app && chown -R app /app
# USER app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copiamos solo el .csproj primero
COPY ["TelefonicaEmpresaria.csproj", "."]

# Restauramos dependencias
RUN dotnet restore "TelefonicaEmpresaria.csproj"

# Copiamos el resto
COPY . .

# Compilamos
RUN dotnet build "TelefonicaEmpresaria.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "TelefonicaEmpresaria.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelefonicaEmpresaria.dll"]
