FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Sraz.UI.Blazor/Sraz.UI.Blazor.csproj
RUN dotnet publish src/Sraz.UI.Blazor/Sraz.UI.Blazor.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Sraz.UI.Blazor.dll"]
