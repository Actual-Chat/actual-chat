FROM mcr.microsoft.com/dotnet/sdk:6.0
RUN dotnet tool install DependenSee --global
ENV PATH "/root/.dotnet/tools:${PATH}"
RUN mv /root/.dotnet/tools/DependenSee /root/.dotnet/tools/dotnet-DependenSee
WORKDIR /workdir
ENTRYPOINT ["dotnet", "DependenSee"]
