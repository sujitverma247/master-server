FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
COPY MasterServer.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine
RUN apk add --no-cache fontconfig ttf-dejavu
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV PORT=8080
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MasterServer.dll"]
