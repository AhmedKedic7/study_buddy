FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY StudyBuddy.Api/StudyBuddy.Api.csproj StudyBuddy.Api/
RUN dotnet restore StudyBuddy.Api/StudyBuddy.Api.csproj
COPY StudyBuddy.Api/ StudyBuddy.Api/
RUN dotnet publish StudyBuddy.Api/StudyBuddy.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StudyBuddy.Api.dll"]
