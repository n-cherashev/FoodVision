FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY backend/Directory.Build.props backend/
COPY backend/src/ backend/src/
RUN dotnet restore backend/src/FoodVision.App/FoodVision.App.csproj -r linux-x64
RUN dotnet publish backend/src/FoodVision.App/FoodVision.App.csproj -c Release -r linux-x64 --self-contained true -o /app/publish --no-restore

FROM nvidia/cuda:13.0.0-cudnn-runtime-ubuntu24.04
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates ffmpeg libgomp1 libicu74 libssl3 \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["./FoodVision.App"]
