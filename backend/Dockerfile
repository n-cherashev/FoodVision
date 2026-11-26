FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY backend/Directory.Build.props backend/
COPY backend/src/ backend/src/
RUN dotnet restore backend/src/FoodVision.App/FoodVision.App.csproj -r linux-x64
RUN dotnet publish backend/src/FoodVision.App/FoodVision.App.csproj -c Release -r linux-x64 --self-contained true -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["./FoodVision.App"]
