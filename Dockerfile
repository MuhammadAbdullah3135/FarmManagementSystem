# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/FMS.Domain/FMS.Domain.csproj src/FMS.Domain/
COPY src/FMS.Application/FMS.Application.csproj src/FMS.Application/
COPY src/FMS.Infrastructure/FMS.Infrastructure.csproj src/FMS.Infrastructure/
COPY src/FMS.API/FMS.API.csproj src/FMS.API/
RUN dotnet restore src/FMS.API/FMS.API.csproj
COPY src/ src/
RUN dotnet publish src/FMS.API/FMS.API.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
# The commit this image is built from. The deploy job passes it as a build arg
# (heroku container:push --arg GIT_SHA=…); /health reports it so the post-deploy smoke check
# can tell a release that landed from one that never left GitHub. "unknown" is what a local
# or hand-made build reports, which keeps that case visible instead of looking stamped.
ARG GIT_SHA=unknown
ENV FMS_BUILD_SHA=${GIT_SHA}

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "FMS.API.dll"]
