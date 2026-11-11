---
description: Run the repository's GitHub Actions build-and-test sequence locally
---

# Run CI Locally

The CI build-and-test job uses .NET 11, Release configuration, and `NOF.slnx` on Ubuntu and Windows. Windows installs the pinned MAUI workload set and required Android SDK platform first.

## Windows Prerequisite

```bash
dotnet workload install maui
dotnet build src/Hostings/NOF.Hosting.Maui/NOF.Hosting.Maui.csproj -t:InstallAndroidDependencies -f net11.0-android -p:AcceptAndroidSdkLicenses=True
```

The project files use non-MAUI stubs on Linux. macOS builds use the MAUI targets and therefore also require the applicable workload.

On Linux, run `dotnet workload restore NOF.slnx -p:Configuration=Release` once to install the pinned workload-set metadata even though MAUI uses stubs.

## CI Sequence

```bash
dotnet restore NOF.slnx
dotnet format --verify-no-changes --verbosity diagnostic
dotnet build NOF.slnx --configuration Release --no-restore
dotnet test NOF.slnx --configuration Release --no-build --verbosity normal --collect:"XPlat Code Coverage"
```

GitHub runs the formatting check only on Linux, but contributors should run it on every platform.

If formatting fails:

```bash
dotnet format
```

Review the resulting changes, then rerun the verification sequence. The CD workflow repeats restore, format, build, and tests before packing projects discovered under `src/`.
