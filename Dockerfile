# ---- Compilación ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ src/

# Si el repositorio trae carpetas bin/obj compiladas en Windows, se borran
# para que no choquen con la compilación de Linux.
RUN rm -rf src/ChatbotDental/bin src/ChatbotDental/obj

RUN dotnet publish src/ChatbotDental/ChatbotDental.csproj -c Release -o /app

# ---- Ejecución ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Railway define PORT; si no está, se usa 8080.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ChatbotDental.dll"]
