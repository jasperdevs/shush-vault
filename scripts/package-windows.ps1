param(
    [string] $Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

dotnet publish .\src\ShushVault.Windows\ShushVault.Windows.csproj `
    -c Release `
    -p:Platform=x64 `
    -r win-x64 `
    --self-contained false `
    -p:PublishTrimmed=false `
    -o .\artifacts\ShushVault.Windows

dotnet tool restore
dotnet vpk pack `
    --packId ShushVault `
    --packVersion $Version `
    --packDir .\artifacts\ShushVault.Windows `
    --mainExe ShushVault.Windows.exe `
    --outputDir .\artifacts\velopack
