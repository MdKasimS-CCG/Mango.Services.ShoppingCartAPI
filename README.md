# Mango.Services.ShoppingCartAPI

ShoppingCartAPI is the cart microservice in the Mango application. It exposes cart operations over HTTP, persists cart headers and cart items in SQL Server, and calls ProductAPI and CouponAPI to retrieve product and coupon information. It also uses the shared `Mango.MessageBus` package for publishing messages consumed by other services.

## Contents

- [Overview](#overview)
- [Technology](#technology)
- [Architecture and integrations](#architecture-and-integrations)
- [Configuration](#configuration)
- [Prerequisites](#prerequisites)
- [Run locally](#run-locally)
- [Run with Docker](#run-with-docker)
- [Setting up docker using any terminal](#setting-up-docker---using-any-terminal)
- [Run in the full Mango Compose stack](#run-in-the-full-mango-compose-stack)
- [CI/CD](#cicd)
- [HTTP API](#http-api)
- [Troubleshooting](#troubleshooting)

## Overview

The service manages a user's shopping cart and its associated line items. The repository includes the cart API controller, EF Core data context and migrations, DTOs, product/coupon HTTP clients, and authentication handler.

Implemented areas include:
- Retrieve a cart by user.
- Add or update cart items.
- Remove cart items and clear cart data through cart operations.
- Apply coupon information to a cart using CouponAPI.
- Retrieve product information using ProductAPI.
- Publish cart-related messages through the shared message-bus package.

Refer to `Controllers/CartAPIController.cs` for the current route and action definitions.

## Technology

- .NET 10 / ASP.NET Core Web API
- Entity Framework Core and SQL Server
- AutoMapper
- HTTP clients for downstream services
- JWT bearer-token handling for downstream API calls
- `Mango.MessageBus` NuGet package, hosted in GitHub Packages
- Docker and GitHub Actions

## Architecture and integrations

```text
Mango.Web
   |
   | HTTP + bearer token
   v
ShoppingCartAPI ---- HTTP ----> ProductAPI
       |           HTTP ----> CouponAPI
       |
       +---- SQL Server (cart data)
       |
       +---- Mango.MessageBus package ----> RabbitMQ (message publishing)
```

The actual downstream URLs are environment-specific. In Docker Compose, use Compose service DNS names and the container's listening port, typically `http://mango-product:8080` and `http://mango-coupon:8080`, rather than `localhost`.

**RabbitMQ configuration note:** the shared `Mango.MessageBus` implementation used by this solution has been observed to hardcode `localhost` in its producer. That means environment variables alone may not redirect the producer to an external RabbitMQ host when running inside a container. This is a known shared-package limitation and must be addressed in that package before relying on container-to-RabbitMQ publishing.

## Configuration

The repository contains `.env.example`. Copy it to `.env` and supply values for your environment. Do not commit credentials or production connection strings.

The application selects a configuration prefix based on whether it is running in a container: `Http` for a local HTTP run and `Docker` when `DOTNET_RUNNING_IN_CONTAINER=true`. The settings are then mapped into the application's standard connection-string, service URL, and message-queue configuration.

Typical configuration categories:

| Setting | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core environment, commonly `Development` locally |
| `ASPNETCORE_HTTP_PORTS` | HTTP port inside the container (8080 in the Dockerfile) |
| `Http__ConnectionStrings__DefaultConnection` | Local SQL Server connection string |
| `Docker__ConnectionStrings__DefaultConnection` | SQL Server connection string for container execution |
| HTTP/Docker ProductAPI and CouponAPI URL settings | Base URLs for downstream APIs; use Compose service names in Compose |
| HTTP/Docker RabbitMQ connection settings | Message broker settings used by the service/package |

Environment variables use double underscores to represent nested .NET configuration keys. Use `.env.example` as the exact variable-name reference for this checkout.

For local development, a SQL Server connection string may use `localhost` when SQL Server publishes its port to the host. From a container, use `host.docker.internal` for a SQL Server container running separately on Docker Desktop. For services in the same Compose project, use the Compose service name.

## Prerequisites

- .NET 10 SDK
- Access to the private GitHub Packages feed containing `Mango.MessageBus`
- SQL Server
- ProductAPI and CouponAPI for end-to-end cart operations
- RabbitMQ for message publishing
- Docker Desktop for container execution

Configure a NuGet source with authorized read access to the GitHub Packages feed before restoring dependencies. Never commit a personal access token or authenticated NuGet configuration.

## Run locally

1. Clone the repository and open the project directory.
2. Copy `.env.example` to `.env` and fill in local connection strings and service URLs.
3. Ensure the private GitHub Packages NuGet source is configured for your account.
4. Restore and run:

```bash
dotnet restore Mango.Services.ShoppingCartAPI.csproj
dotnet run --launch-profile http
```

The checked-in HTTP launch profile uses `http://localhost:5216`. The HTTPS profile also declares `https://localhost:7154`.

Swagger is enabled in Development. Open:

```text
http://localhost:5216/swagger
```

The service applies pending EF Core migrations during application startup. The configured SQL Server database must be reachable and the database user must have the permissions required for migrations.

## Run with Docker

The Dockerfile uses the .NET 10 ASP.NET runtime and SDK images and listens on port 8080 inside the container. It uses a BuildKit secret named `nugetconfig` for restoring the private package.

From the repository directory, build with a NuGet configuration file that has access to the private feed:

```bash
docker buildx build \
  --secret id=nugetconfig,src="<path-to-NuGet.Config>" \
  -t mango-shoppingcart:local \
  --load .
```

Run the image with a project `.env` file and a host port mapping:

```bash
docker run --rm --name mango-shoppingcart \
  --env-file .env \
  -p 5126:8080 \
  mango-shoppingcart:local
```

The example host port `5126` is a local mapping; the application listens on 8080 inside the container. Ensure container-mode database and downstream service URLs are configured for the container's network context.

## Setting up docker - using any terminal

Building the image:-
docker build --secret id=nugetconfig,src="$env:NUGET_CONFIG_PATH" -t mango-shoppingcartapi:local .

Running the container:-
docker run --name mango-shoppingcartapi --env-file .env -p 5220:8080 mango-shoppingcartapi:local

## Run in the full Mango Compose stack

Use the standalone Compose file at the Mango solution root—not a Visual Studio-generated Compose file—when running the full application.

A Compose service should build with this repository as its build context so the Dockerfile's project-relative `COPY` instructions resolve:

```yaml
services:
  mango-shoppingcart:
    build:
      context: ./Backend/Mango.Services.ShoppingCartAPI
      dockerfile: Dockerfile
      secrets:
        - nuget_config
    env_file:
      - ./Backend/Mango.Services.ShoppingCartAPI/.env
    environment:
      ASPNETCORE_HTTP_PORTS: "8080"
      # Set downstream URLs to Compose DNS names in the actual Compose file.
```

The root Compose file must declare the `nuget_config` BuildKit secret and point it to an authorized NuGet.Config. Keep secret files out of source control.

For full-stack networking:
- Use Compose service names for Mango APIs, for example `mango-product` and `mango-coupon`.
- Use `host.docker.internal` for SQL Server or RabbitMQ containers that are running separately from the Compose network, subject to the shared MessageBus producer limitation above.
- Start dependencies before testing cart workflows.

From the solution root:

```bash
docker compose -f docker-compose.yml config
docker compose -f docker-compose.yml build mango-shoppingcart
docker compose -f docker-compose.yml up -d mango-shoppingcart
docker compose -f docker-compose.yml logs -f mango-shoppingcart
```

Adjust the Compose filename or service key if the solution's root Compose file uses a different name.

## CI/CD

The repository workflow is `.github/workflows/shoppingcart-api.yaml`.

The workflow is configured for pushes to `feature/*` and `main`, and pull requests targeting `main`. It installs .NET 10, caches NuGet packages, configures the GitHub Packages NuGet source using the workflow token, restores and builds the project, and builds a Docker image using Buildx and a NuGet BuildKit secret.

The workflow currently logs that no automated test project exists and skips automated tests. On pushes to `main`, it logs in to GHCR and pushes the image tagged with the commit SHA. Pull-request image builds are validation builds and are not published to GHCR.

## HTTP API

The API is implemented in `Controllers/CartAPIController.cs`. Consult that controller or the Swagger document for the exact route templates, HTTP verbs, request DTOs, and authorization requirements. Swagger is available in Development at `/swagger`.

| Area | Purpose |
|---|---|
| Cart retrieval | Fetch the current cart for a user |
| Cart mutation | Add/update cart items and remove items |
| Coupon | Apply or remove coupon data from a cart |
| Cart messaging | Publish cart-related events/messages as implemented by the controller |

The service uses bearer-token authentication for protected operations. Provide a valid token issued by the Mango authentication service when invoking protected endpoints.

## Troubleshooting

| Symptom | Checks |
|---|---|
| `NU1101` / private package restore fails | Confirm NuGet.Config has authenticated read access to the GitHub Packages feed and that Docker BuildKit receives the `nugetconfig` secret. |
| SQL Server connection fails in a container | Do not use `localhost` for a separate SQL Server container; use `host.docker.internal` and verify the host port is published. |
| ProductAPI/CouponAPI calls fail in Compose | Use Compose service DNS names and the port exposed inside the target container, not host-mapped ports or `localhost`. |
| RabbitMQ connection attempts target `127.0.0.1` inside a container | The shared `Mango.MessageBus` producer currently hardcodes localhost. This is not fixed by changing only the service `.env`; update/configure the shared package and consume its updated version. |
| Swagger is unavailable | Swagger middleware is enabled only in the Development environment. |
| Database startup/migration fails | Verify the database exists, credentials are valid, and the configured login can apply migrations. |