FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /app
COPY . ./
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Kopiuj assemblies .NET Framework 4.8 z cache NuGet do publish/NetFx48/
# MSBuild target może nie zadziałać poprawnie na Linux — robimy to explicite
RUN mkdir -p /app/publish/NetFx48 && \
    find /root/.nuget/packages/microsoft.netframework.referenceassemblies.net48 \
         -path "*/v4.8/*.dll" \
         ! -name "*Thunk*" \
         ! -name "*Wrapper*" \
         -exec cp {} /app/publish/NetFx48/ \;

# Upewnij się że ZWCAD DLL-ki są w publish/Libraries/Zwcad/
RUN mkdir -p /app/publish/Libraries/Zwcad && \
    cp /app/Libraries/Zwcad/*.dll /app/publish/Libraries/Zwcad/ 2>/dev/null || true

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENTRYPOINT ["dotnet", "cadll.dll"]
