FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["FacturacionVERIFACTU.API/FacturacionVERIFACTU.API.csproj", "FacturacionVERIFACTU.API/"]
COPY ["FacturacionVERIFACTU.Shared/FacturacionVERIFACTU.Shared.csproj", "FacturacionVERIFACTU.Shared/"]
RUN dotnet restore "FacturacionVERIFACTU.API/FacturacionVERIFACTU.API.csproj"
COPY . .
WORKDIR "/src/FacturacionVERIFACTU.API"
RUN dotnet publish -c Release -o /app/out

FROM base AS final
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "FacturacionVERIFACTU.API.dll"]
