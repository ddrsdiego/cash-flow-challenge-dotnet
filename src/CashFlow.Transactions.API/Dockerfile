FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/CashFlow.Transactions.API/CashFlow.Transactions.API.csproj", "src/CashFlow.Transactions.API/"]
COPY ["src/CashFlow.SharedKernel/CashFlow.SharedKernel.csproj", "src/CashFlow.SharedKernel/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/CashFlow.Transactions.API/CashFlow.Transactions.API.csproj"
COPY . .
WORKDIR "/src/src/CashFlow.Transactions.API"
RUN dotnet build "CashFlow.Transactions.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CashFlow.Transactions.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CashFlow.Transactions.API.dll"]
