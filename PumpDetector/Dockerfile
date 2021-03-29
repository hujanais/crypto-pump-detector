FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env

# Create working directory
WORKDIR /app

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o output

FROM mcr.microsoft.com/dotnet/runtime:3.1
WORKDIR /app
COPY --from=build-env /app/output .

ENTRYPOINT ["dotnet", "PumpDetector.dll"]


