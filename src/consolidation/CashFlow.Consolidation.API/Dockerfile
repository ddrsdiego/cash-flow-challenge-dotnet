FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/CashFlow.Consolidation.API/CashFlow.Consolidation.API.csproj", "src/CashFlow.Consolidation.API/"]
COPY ["src/CashFlow.SharedKernel/CashFlow.SharedKernel.csproj", "src/CashFlow.SharedKernel/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/CashFlow.Consolidation.API/CashFlow.Consolidation.API.csproj"
COPY . .
WORKDIR "/src/src/CashFlow.Consolidation.API"
RUN dotnet build "CashFlow.Consolidation.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CashFlow.Consolidation.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CashFlow.Consolidation.API.dll"]
