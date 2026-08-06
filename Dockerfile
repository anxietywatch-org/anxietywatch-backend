FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY Directory.Build.props .
COPY src/AnxietyWatch.Domain/AnxietyWatch.Domain.csproj src/AnxietyWatch.Domain/
COPY src/AnxietyWatch.Application/AnxietyWatch.Application.csproj src/AnxietyWatch.Application/
COPY src/AnxietyWatch.Infrastructure/AnxietyWatch.Infrastructure.csproj src/AnxietyWatch.Infrastructure/
COPY src/AnxietyWatch.Api/AnxietyWatch.Api.csproj src/AnxietyWatch.Api/

RUN dotnet restore src/AnxietyWatch.Api/AnxietyWatch.Api.csproj

COPY src/ src/
RUN dotnet publish src/AnxietyWatch.Api/AnxietyWatch.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --no-self-contained \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish ./

EXPOSE 8080
USER 10001
ENTRYPOINT ["dotnet", "AnxietyWatch.Api.dll"]
