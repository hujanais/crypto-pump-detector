FROM mcr.microsoft.com/dotnet/core/sdk:3.1

ENTRYPOINT ["dotnet", "publish", "-c", "Release"]

copy bin/Release/netcoreapp3.1/publish App/

ENTRYPOINT ["dotnet", "PumpDetector.dll"]


