FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Divoom.Client/ src/Divoom.Client/
COPY src/Divoom.Transport.Linux/ src/Divoom.Transport.Linux/
COPY src/Divoom.Api/ src/Divoom.Api/
RUN dotnet publish src/Divoom.Api/Divoom.Api.csproj -c Release -f net10.0 -o /out --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ .
ENV DBUS_SYSTEM_BUS_ADDRESS=unix:path=/run/dbus/system_bus_socket \
    DIVOOM_STATE_DIRECTORY=/data \
    TZ=Europe/Amsterdam \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Divoom.Api.dll"]
