# ---- Compilación ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ChatbotDental/ChatbotDental.csproj src/ChatbotDental/
RUN dotnet restore src/ChatbotDental/ChatbotDental.csproj

COPY src/ src/
RUN dotnet publish src/ChatbotDental/ChatbotDental.csproj -c Release -o /app --no-restore

# ---- Ejecución ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Railway define PORT; si no está, se usa 8080.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ChatbotDental.dll"]
