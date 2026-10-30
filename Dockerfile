# Direct Service Transportation — container image of the web host (NFR-22: provider-neutral, container-ready, configuration-driven).
# Not built on the development PC (no container runtime there); the hosting provider is still to be chosen (DEF-01).
# Build:  docker build -t direct-service-transportation .
# Run:    docker run -p 8080:8080 -e ConnectionStrings__MasterDb=... -e UrlTokens__Key=... -v dst-keys:/keys -v dst-files:/files direct-service-transportation
# The key ring in /keys must be protected at rest (DT-221): mount a certificate and add -e DataProtection__CertificatePath=/certs/keys.pfx
# -e DataProtection__CertificatePassword=... (or -e DataProtection__AllowUnprotectedKeys=true when the volume is encrypted by the host);
# without either the host refuses to start.
# Behind a TLS-terminating proxy add -e Hosting__BehindProxy=true and the proxy's addresses, e.g. -e Hosting__KnownNetworks__0=10.0.0.0/8
# (or Hosting__KnownProxies__0=...); the host refuses to start behind a proxy without them, so X-Forwarded-For cannot be forged.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props DirectService.Transportation.slnx ./
COPY src/ src/
# The logo embedded in the system e-mails (Platform.Infrastructure references ../../../assets/logo.png); the brand fonts are in Shared.UI/wwwroot/fonts.
COPY assets/ assets/
RUN dotnet publish src/DirectService.Transportation.Web/DirectService.Transportation.Web.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Chromium prints the PDFs (Q-08); fonts for Arabic text come embedded in the printouts (step 7), fontconfig is still needed by Chromium.
RUN apt-get update \
 && apt-get install -y --no-install-recommends chromium fontconfig \
 && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Printing__ChromiumPath=/usr/bin/chromium \
    DataProtection__KeysPath=/keys \
    Files__Root=/files \
    Printing__NoSandbox=true
WORKDIR /app
COPY --from=build /app .
# The key ring and the export file store must be persistent volumes shared by all instances (see docs/step9/Setup_and_Run.md §8).
# Chromium runs without its own sandbox (Docker's default seccomp profile blocks the namespaces it needs); it opens only the
# application's own printout HTML with scripts off and every network request refused except data: (step 7).
RUN mkdir -p /keys /files && chown app:app /keys /files
VOLUME ["/keys", "/files"]
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.0\\r\\n\\r\\n' >&3 && grep -q '200' <&3"]
ENTRYPOINT ["dotnet", "DirectService.Transportation.Web.dll"]
