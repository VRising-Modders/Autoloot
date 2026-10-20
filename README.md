# Autoloot

![Drops come to you](https://raw.githubusercontent.com/VRising-Modders/Autoloot/main/docs/images/01-pickup.webp)

**Drops near you go straight into your bags.** Loot from kills, resource nodes and anything else that lands on the ground is picked up while you keep moving: no more running over every pile. Gear and jewels keep their stats.

## How it works

- Every drop within your **pickup range** goes into your bags.
- A notice at the top of the screen lists what you picked up and how many of each you now carry: `Autoloot: Bone +20 (100)` means 20 bones picked up, 100 in your bags. Pickups close together share one line.
- Turn on **Show my pickup range** to see the range as a ring of flames on the ground around you. The **Help** button on the mod's page shows the same range drawn to scale.
- Never picked up: items a player dropped, and soul shards.

![The pickup range](https://raw.githubusercontent.com/VRising-Modders/Autoloot/main/docs/images/02-ring.webp)

![Pickup notices with your totals](https://raw.githubusercontent.com/VRising-Modders/Autoloot/main/docs/images/05-notices.webp)

## For players

Open **Mods** → **Autoloot**:

- **Autoloot**: on or off, for you alone.
- **Pickup filter**: items with the most of each you want in your bags. Set an item to 0 to never pick it up; anything not listed is always picked up while there is room.
- **Pickup notices**: show or hide the line that lists what you picked up and your new totals.
- **Show my pickup range**: draw the ring. You can also give it a key in **Mods** → **Keybinds**.

![Your settings](https://raw.githubusercontent.com/VRising-Modders/Autoloot/main/docs/images/03-settings.webp)

## For server admins

Admins change these in the game, on the same page:

| Setting | Default | Choices |
|---|---|---|
| Autoloot for players | On | Off stops autoloot for everyone |
| Pickup range | 10 m | 1 to 30 m |
| What is picked up | Any drop that comes in range | Or only drops that land while the player is in range |
| Several players in range | Closest player first | Or players take turns |
| Death bags | Only the player's own | Never, or anyone the game lets open them |
| Dropped inventory bags | Never | Or anyone except the player who dropped them |
| Players without the client part | Autoloot on, with the defaults | Off, and whether they are told about it when they join |

![Server settings](https://raw.githubusercontent.com/VRising-Modders/Autoloot/main/docs/images/04-server-settings.webp)

## Where to install it

- **The server** needs Autoloot: it does the picking up.
- **Players** should install it too, for the settings, the pickup filter and the ring. Players without it still get autoloot with the defaults, unless the admin turns that off.
- A private game you host from the game counts as your server.

## Requires Progenitor

> **Requires Progenitor.** Autoloot is built on [Progenitor](https://github.com/VRising-Modders/Progenitor), a modding framework for V Rising. Install Progenitor first; its Install Mods tab can then install this mod.

**Installing Progenitor** ([step by step](https://vrising-modders.github.io/Progenitor/guide/progenitor-guide.html#players/install)):

**With the installer.** Quit the game, open **PowerShell** from the Start menu, paste this line and press Enter:

```powershell
irm https://vrising-modders.github.io/Progenitor/install.ps1 | iex
```

It finds the game through Steam and installs BepInEx too when it is missing. The script is public; [read it first](https://vrising-modders.github.io/Progenitor/install.ps1).

For a dedicated server, stop the server and use this line instead:

```powershell
& ([scriptblock]::Create((irm https://vrising-modders.github.io/Progenitor/install.ps1))) -Server
```

**By hand.** Install [BepInExPack V Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/), then download `Progenitor-<version>.zip` from the [latest release](https://github.com/VRising-Modders/Progenitor/releases/latest) and extract it into the folder that holds `VRising.exe`. For a dedicated server, extract the same zip into the folder that holds `VRisingServer.exe`.

Then start the game, open **Mods** on the title screen, go to **Install Mods**, search for **Autoloot** and click **Install**. Install it on the server the same way, as an admin, or extract this package's `Autoloot.dll` into the server's `BepInEx/plugins` folder.

## Source and license

Source and issues: [github.com/VRising-Modders/Autoloot](https://github.com/VRising-Modders/Autoloot). Licensed under the AGPL-3.0.
