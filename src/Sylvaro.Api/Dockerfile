FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY NormyxAI.sln .
COPY src/Normyx.Api/Normyx.Api.csproj src/Normyx.Api/
COPY src/Normyx.Application/Normyx.Application.csproj src/Normyx.Application/
COPY src/Normyx.Domain/Normyx.Domain.csproj src/Normyx.Domain/
COPY src/Normyx.Infrastructure/Normyx.Infrastructure.csproj src/Normyx.Infrastructure/
RUN dotnet restore src/Normyx.Api/Normyx.Api.csproj

COPY . .
RUN dotnet publish src/Normyx.Api/Normyx.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
COPY policy-packs ./policy-packs
COPY prompts ./prompts
COPY reference-notes ./reference-notes
EXPOSE 8080
ENTRYPOINT ["dotnet", "Normyx.Api.dll"]
