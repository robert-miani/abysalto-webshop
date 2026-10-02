# Build stage: restore with only the project files first, so the restore layer is cached.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/CartService.Domain/CartService.Domain.csproj src/CartService.Domain/
COPY src/CartService.Application/CartService.Application.csproj src/CartService.Application/
COPY src/CartService.Infrastructure/CartService.Infrastructure.csproj src/CartService.Infrastructure/
COPY src/CartService.Api/CartService.Api.csproj src/CartService.Api/
RUN dotnet restore src/CartService.Api/CartService.Api.csproj

COPY src/ src/
RUN dotnet publish src/CartService.Api/CartService.Api.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

# Runtime stage: ASP.NET Core runtime only, running as the non-root "app" user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "CartService.Api.dll"]
