# Autoloot

![Drops come to you](https://raw.githubusercontent.com/VRising-Modders/Autoloot/main/docs/images/01-pickup.webp)

**Drops near you go straight into your bags.** Loot from kills, resource nodes and anything else that lands on the ground is picked up while you keep moving: no more running over every pile. Gear and jewels keep their stats.

## How it works

- Every drop within your **pickup range** goes into your bags.
- A notice at the top of the screen lists what you picked up and how many of each you now carry: `Autoloot: Bone +20 (100)` means 20 bones picked up, 100 in your bags. Pickups close together share one line.
- Turn on **Show my pickup range** to see the range as a ring of flames on the ground around you. The **Help** button on the mod's page shows the same range drawn to scale.
- Never picked up: items a player dropped, and soul shards.

![The pickup range](https://raw.githubusercontent.com/VRising-Modders/Autoloot/main/docs/images/02-ring.webp)

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

## With Satisvampory

On a server that also runs [Satisvampory](https://thunderstore.io/c/v-rising/p/Team_GreenEye/Satisvampory/), autoloot can stop at your island's caps, and your pickup filter can hold Satisvampory categories (such as Flowers or Materials).

## Where to install it

- **The server** needs Autoloot: it does the picking up.
- **Players** should install it too, for the settings, the pickup filter and the ring. Players without it still get autoloot with the defaults, unless the admin turns that off.
- A private game you host from the game counts as your server.

## Requires Progenitor

Autoloot is built on [Progenitor](https://github.com/VRising-Modders/Progenitor), a mod manager and modding framework for V Rising. Install Progenitor first, on the server and on every client:

1. Install [BepInExPack V Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) into the folder that holds `VRising.exe` (for a dedicated server, the folder that holds `VRisingServer.exe`).
2. Quit the game (or stop the server).
3. Download `Progenitor-<version>.zip` from [Progenitor's GitHub releases](https://github.com/VRising-Modders/Progenitor/releases/latest) and extract it into the same folder. It holds one `BepInEx` folder; let it replace files when you update.
4. Start the game and open **Mods** on the title screen.
5. In **Install Mods**, search for **Autoloot** and click **Install**.

Progenitor is also on [Thunderstore](https://thunderstore.io/c/v-rising/p/VRising_Modders/Progenitor/), and a mod manager installs it for you along with this package.

## Source and license

Source and issues: [github.com/VRising-Modders/Autoloot](https://github.com/VRising-Modders/Autoloot). Licensed under the AGPL-3.0.
