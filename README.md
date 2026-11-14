# Expedition 33: Clair Obscur - Coop

For those who come after, with your friends!

![](./docs/header_s.jpg)

## How it works

- During battles each player will control their own character, including having to parry and chose actions.
- During exploration, player 1 will have control by default, but any player can take over by pressing START (useful for leveling and choosing equipment for your own characters)
- Battle spots come from the game's own party table, so mods with a larger party (such as Expedition Infinity's party of five) map characters to the right player. Party slots 4 and 5 have their own assignment settings; they default to player 1.
- In battle, a label at the top shows the player in control on a player turn, or the players attacked on an enemy turn (`Player 2`, `Player 1, 3`, `All players`). It fades out after 5 seconds and shows again on the next turn.
- Enemy attack names end with the players attacked, for example `Slash (P1)` or `Slash (All)`.
- The team menu badge shows the party slot over the assigned player (`C1` over `P2`).

### Playing with friends remotely

Steam only offers "Remote Play-Together" for games that have built-in coop support, but you can use [RemotePlayWhatever](https://github.com/m4dEngi/RemotePlayWhatever) as a workaround. 

## Installation

Download the latest release and unzip it in the game folder under `..\Expedition 33\Sandfall\Content\Paks\~mods`. 
If the `~mods` folder does not exist, create it.
When updating from a release that shipped `expedition33_coop_P`, delete those files: the pack is now `expedition33_coop_9_P`, which mounts after other mods' `_P` packs.

The pack ships its own copy of the battle character blueprint with an ally left-to-right table for 5 battle spots, so it can sit next to party-of-five mods such as Expedition Infinity that change that blueprint only for this table.

## Issues

The mod is still in the very early stages, if you experience any issues, please file an issue with an exact description and your save game attached.

### Known Issues

- Press & hold actions in menus (Learn Skill, Upgrade Attributes, Level Weapon) can currently only be controlled by player 1
  - Can also be slightly mitigated by moving the mouse after the held action failed
- Prompts answered with A, such as leaving a battle or photo mode, only respond to player 1: CommonUI drops accept events from other local players

## For Developers

### Building

- Clone this repository `git clone https://github.com/Kouzukii/expedition33-coop.git --recurse-submodules`
- Download [retoc](https://github.com/trumank/retoc) into the project root folder.
- Extract the game's assets into the `./Export` folder 
  ```
  .\retoc.exe to-legacy "GAMEPATH\Expedition 33\Sandfall\Content\Paks" ".\Export"
  ```
- Start Unreal Engine 5.4 and open `./Sandfall/Sandfall.uproject`
- Package the project with the Shipping configuration under the `./Sandfall` directory (Such that `./Sandfall/Windows/Sandfall/Sandfall.exe` is created)
- Build the mod using `dotnet run --project Expedition33Coop.csproj` (DotNet SDK 9 is required)