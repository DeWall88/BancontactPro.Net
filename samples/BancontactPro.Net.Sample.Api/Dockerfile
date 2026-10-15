# syntax=docker/dockerfile:1
# Build context is the repository root: docker build -f samples/BancontactPro.Net.Sample.Api/Dockerfile -t bancontactpro-sample-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore as its own layer: only the project files, so it is cached until a package reference changes.
COPY src/BancontactPro.Net/BancontactPro.Net.csproj src/BancontactPro.Net/
COPY samples/BancontactPro.Net.Sample.Api/BancontactPro.Net.Sample.Api.csproj samples/BancontactPro.Net.Sample.Api/
RUN dotnet restore samples/BancontactPro.Net.Sample.Api/BancontactPro.Net.Sample.Api.csproj

COPY src/ src/
COPY samples/ samples/
# MinVer reads git tags, which the image build does not have; the sample's own version is irrelevant.
RUN dotnet publish samples/BancontactPro.Net.Sample.Api/BancontactPro.Net.Sample.Api.csproj \
    -c Release --no-restore -o /app /p:MinVerSkip=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The .NET image's non-root user.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BancontactPro.Net.Sample.Api.dll"]
