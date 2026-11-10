FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ComputerLogger.Service/ComputerLogger.Service.csproj src/ComputerLogger.Service/
RUN dotnet restore src/ComputerLogger.Service/ComputerLogger.Service.csproj
COPY src/ src/
RUN dotnet publish src/ComputerLogger.Service/ComputerLogger.Service.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# Reading RAPL energy counters requires root on most kernels
USER root
ENTRYPOINT ["dotnet", "ComputerLogger.Service.dll"]
