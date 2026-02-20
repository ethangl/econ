# Building EconSim.Core as DLL (optional)

Currently using source files in Unity. To switch to DLL approach once Core stabilizes:

```bash
# Install dotnet CLI (one-time)
brew install dotnet

# Build and deploy DLL
cd src/EconSim.Core
dotnet build -c Release
mkdir -p ../../unity/Assets/Plugins
cp bin/Release/netstandard2.1/EconSim.Core.dll ../../unity/Assets/Plugins/
rm -rf ../../unity/Assets/Scripts/EconSim.Core/  # remove source files
```

DLL approach is better for multi-engine support (Godot/Bevy). Source files are easier during active development.
