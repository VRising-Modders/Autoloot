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

## With Satisvampory 2.0

On a server that also runs [Satisvampory 2.0](https://github.com/VRising-Modders/Satisvampory), the castle logistics mod built on Progenitor, autoloot can stop at your island's caps, and your pickup filter can hold Satisvampory categories (such as Flowers or Materials). Satisvampory 1.x does not have this link.

## Where to install it

- **The server** needs Autoloot: it does the picking up.
- **Players** should install it too, for the settings, the pickup filter and the ring. Players without it still get autoloot with the defaults, unless the admin turns that off.
- A private game you host from the game counts as your server.

## Requires Progenitor

Autoloot is built on [Progenitor](https://github.com/VRising-Modders/Progenitor), a mod manager and modding framework for V Rising. Install Progenitor first, on the server and on every client. A mod manager installs it for you along with this package; to install it yourself from GitHub, pick one of these.

**The installer.** Quit the game (or stop the server), open **PowerShell** from the Start menu, paste this line and press Enter:

```powershell
irm https://vrising-modders.github.io/Progenitor/install.ps1 | iex
```

It finds V Rising through Steam, installs BepInEx if it's missing, installs the latest Progenitor and tells you what it did. Files it replaces go to `BepInEx\progenitor-backups` first. The script is public; [read it first](https://vrising-modders.github.io/Progenitor/install.ps1). For a dedicated server, or a game in a folder Steam doesn't know, see the options in [Progenitor's README](https://github.com/VRising-Modders/Progenitor#install-players).

**By hand.**

1. Install [BepInExPack V Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/): copy everything inside its `BepInExPack_V_Rising` folder into the folder that holds `VRising.exe` (for a dedicated server, `VRisingServer.exe`), then start the game once.
2. Quit the game (or stop the server).
3. From Progenitor's [latest release](https://github.com/VRising-Modders/Progenitor/releases/latest), under **Assets**, download `Progenitor-<version>.zip` (not the `-thunderstore` zip or the source code).
4. Extract it into the same folder. Its `BepInEx` folder joins the one already there; when Windows asks, replace the files.

Then start the game, open **Mods** on the title screen, go to **Install Mods**, search for **Autoloot** and click **Install**.

## Source and license

Source and issues: [github.com/VRising-Modders/Autoloot](https://github.com/VRising-Modders/Autoloot). Licensed under the AGPL-3.0.
