# LootNetServer

**Author:** 20fpsguy
**Version:** 1.0.0
**SPT Version:** 4.0.13
**License:** MIT

---

## What This Mod Does

Server-side companion mod for LootNet. Exposes a `/lootnet/prices` endpoint that returns live flea market prices to the client mod.

---

## Requirements

- [SPT](https://www.sp-tarkov.com/) **4.0.13** or compatible
- .NET 9 SDK (for building from source)

---

## Building

```sh
git clone 
cd LootNetServer
dotnet build -c Release
```

The build target automatically packages the mod into a distributable `LootNetServer.zip`.

---

## Installation

1. Build the project (see above) **or** download the latest release zip.
2. Extract the zip so that `LootNetServer.dll` ends up in:
   ```
   <SPT root>/user/mods/LootNetServer/
   ```
3. Launch SPT server as usual.

---

## Configuration

No configuration file is required.

---

## Project Structure

```
LootNetServer/
├── LootNetServer.csproj   ← project file + packaging target
├── Mod.cs                 ← entry point (ModMetadata + router + callback)
├── README.md
└── .gitignore
```

---

## Learning Resources

| Resource | URL |
|---|---|
| SPT Server (C#) — Overview | https://deepwiki.com/sp-tarkov/server-csharp/1-overview |
| Server Mod Examples | https://github.com/sp-tarkov/server-mod-examples |
| SPT Wiki Modding Resources | https://wiki.sp-tarkov.com/modding/Modding_Resources |
| SPT Client Mod Examples | https://github.com/Jehree/SPTClientModExamples |

---
