# Build with the .NET SDK

The `mac-dev` branch supports macOS, Linux, and Windows builds with `dotnet build`.
Install the .NET SDK, Valheim, BepInEx, and publicized game assemblies first.

```sh
dotnet build ValheimFortress.sln -c Debug
```

The DLL and package dependencies are written to `ValheimFortress/bin/Debug`.
The build uses the checked-in asset bundle. It does not launch Unity, deploy to a
game profile, or publish a package.

The default macOS game path is
`~/Library/Application Support/Steam/steamapps/common/Valheim`.
Game references come from `Valheim.app/Contents/Resources/Data/Managed`, with
publicized assemblies in its `publicized_assemblies` subdirectory.

For another installation, pass `-p:VALHEIM_INSTALL=...`,
`-p:VALHEIM_MANAGED=...`, `-p:PUBLICIZED_PATH=...`, and/or `-p:BEPINEX_CORE=...`.
You can also set these properties in the ignored
`ValheimFortress/Environment.props` file. Do not commit game assemblies.
