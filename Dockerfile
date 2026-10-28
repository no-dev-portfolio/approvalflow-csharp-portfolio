FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /source
COPY global.json .
COPY src/ApprovalFlow/ApprovalFlow.csproj src/ApprovalFlow/packages.lock.json src/ApprovalFlow/
RUN dotnet restore src/ApprovalFlow --locked-mode
COPY src/ApprovalFlow/ src/ApprovalFlow/
RUN dotnet publish src/ApprovalFlow -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ApprovalFlow.dll"]
