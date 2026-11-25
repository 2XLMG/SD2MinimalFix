## SD2 Minimal Fix

A BepInEx 5 plugin for **Soundodger 2** that fixes timing inconsistencies in the game's engine.

## The Problem
The game uses the music's current position (`AudioSource.time`) as its primary clock. In Unity, this value updates in discrete steps (audio buffer size, ~21ms). The exact position of these steps varies between runs and PCs. Additionally, the enemy ring uses its own independent `Stopwatch` that is never synced to the music.

As a result, bullets, clears, and the ring angle drift apart by ±20°, making gameplay feel inconsistent.

## The Fix
This mod introduces a custom `Clock` that runs smoothly, slowly correcting itself towards the music. It feeds the game a deterministic stepped time and syncs the ring to it.

## Installation
1. Install [BepInEx 5](https://docs.bepinex.dev/articles/user_guide/installation/index.html) for Soundodger 2.
2. Drop `SD2MinimalFix.dll` into `BepInEx/plugins/SD2MinimalFix`.

## Building from source
Requires .NET SDK. 
1. Put the required game DLLs into the `refs/` folder (you can use the `build.bat refs` script or copy them manually from `Soundodger 2_Data/Managed` and `BepInEx/core`).
2. Run `dotnet build -c Release`.
3. The compiled `.dll` will be in `bin/Release/`.