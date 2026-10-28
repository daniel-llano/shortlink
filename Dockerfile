FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ShortLink.Api/ShortLink.Api.csproj src/ShortLink.Api/
RUN dotnet restore src/ShortLink.Api/ShortLink.Api.csproj
COPY src src
RUN dotnet publish src/ShortLink.Api/ShortLink.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ShortLink.Api.dll"]
