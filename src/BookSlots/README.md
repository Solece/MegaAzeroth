# Four tome slots plugin

Runtime-only capacity override for the local Azeroth/Megabonk prototype. Targets Megabonk 1.0.69 with BepInEx IL2CPP 6.0.755 and generated interop.

Build: `dotnet build BookSlots.csproj -c Release`.

Deploy only `bin/Release/net6.0/AzerothMegabonk.BookSlots.dll` into the laboratory BepInEx plugin folder. Remove that DLL to restore native slot behavior. No saves or shop progress are changed. It unlocks four tome slots without granting books or changing weapon capacity. Only use the modded laboratory in offline runs with leaderboard upload disabled.

The original game has a shared native function for max weapon/tome slots; this plugin deliberately overrides only the distinct available-tome-slots function. See `../../BOOKS_LOG.md` for the reverse-engineering evidence and verification status.
