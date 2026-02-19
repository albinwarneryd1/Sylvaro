FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY NormyxAI.sln .
COPY src/Normyx.Web/Normyx.Web.csproj src/Normyx.Web/
COPY src/Normyx.Application/Normyx.Application.csproj src/Normyx.Application/
RUN dotnet restore src/Normyx.Web/Normyx.Web.csproj

COPY . .
RUN dotnet publish src/Normyx.Web/Normyx.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Normyx.Web.dll"]
