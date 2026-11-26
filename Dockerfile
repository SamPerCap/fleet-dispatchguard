# The whole demo in one image (SPEC 0091, ADR 0023):
#   API and web      docker run <image>
#   migrations       docker run --entrypoint /app/migrations/apply <image> --connection "<owner of the database>"
#                    (without ASPNETCORE_ENVIRONMENT: the bundle builds the host of the API, which in Production asks for its settings)
#   demo loader      docker run --entrypoint dotnet <image> /app/sample-data/DispatchGuard.SampleData.dll --api <url> [--reset-key <k> | --check]
# Base images by digest, so that the same commit is always the same image; Dependabot proposes the new ones.

FROM node:25-bookworm-slim@sha256:81db02c4b671288a03915da9534dbd54f96d0e7c24d80ccc54f5b36b2e684370 AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
WORKDIR /repo
RUN dotnet tool install dotnet-ef --version 10.0.15 --tool-path /tools
COPY global.json Directory.Build.props .editorconfig ./
COPY src/Core/Core.csproj src/Core/
COPY src/Application/Application.csproj src/Application/
COPY src/DemoData/DemoData.csproj src/DemoData/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Web.Api/Web.Api.csproj src/Web.Api/
COPY tools/SampleData/SampleData.csproj tools/SampleData/
RUN dotnet restore src/Web.Api/Web.Api.csproj && dotnet restore tools/SampleData/SampleData.csproj
COPY src/Core/ src/Core/
COPY src/Application/ src/Application/
COPY src/DemoData/ src/DemoData/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Web.Api/ src/Web.Api/
COPY tools/SampleData/ tools/SampleData/
RUN dotnet publish src/Web.Api/Web.Api.csproj --no-restore -c Release -o /out/api -p:UseAppHost=false \
 && dotnet publish tools/SampleData/SampleData.csproj --no-restore -c Release -o /out/sample-data -p:UseAppHost=false \
 && mkdir /out/migrations \
 && /tools/dotnet-ef migrations bundle --configuration Release --project src/Infrastructure --startup-project src/Web.Api -o /out/migrations/apply

# Without shell or package manager. "extra" brings the time zones and the cultures: the hours are of Madrid.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:c8dd08a50d8ffd19bd8da72edee05a7ac15b486a5b1869cf46fdb370bf1fbc76
COPY --from=build /out/ /app/
COPY --from=web /web/dist/ /app/api/wwwroot/
# The content root, where the configuration and the web are.
WORKDIR /app/api
USER $APP_UID
# The base has no Kerberos, which Npgsql would try before TLS; the migrations bundle and the API read it.
ENV PGGSSENCMODE=disable
EXPOSE 8080
ENTRYPOINT ["dotnet", "Web.Api.dll"]
