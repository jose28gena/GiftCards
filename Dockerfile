FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/GiftCards.Api/GiftCards.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Directorio persistente para la base SQLite; se monta como volumen.
RUN mkdir /data && chown app /data
USER app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/data/giftcards.db;Default Timeout=30"
EXPOSE 8080
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "GiftCards.Api.dll"]
