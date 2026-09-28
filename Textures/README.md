# Texture layout

- Use plural top-level content folders: `Buildings`, `Items`, `Weapons`, `Icons`, and `Projectiles`.
- Keep era-specific weapon assets under `Weapons/<Era>`, with `Ammo` and `Projectiles` subfolders.
- Keep Helod race art under `Helod`, using `Apparel`, `VanillaApparel`, `Bodies`, `Heads`, `Hair`, `Ears`, and `Tails`.
- Use PascalCase for folder names and preserve exact casing in XML and C# references for case-sensitive platforms.
- Texture references are extensionless paths relative to `Textures`.
- Keep source-art files beside their runtime texture only when they are actively maintained with that asset.
