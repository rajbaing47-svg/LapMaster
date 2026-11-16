# LapMaster

A 3D arcade racing game for PC, built in **Unity (C#)**. LapMaster combines WheelCollider-based car physics with arcade-style handling, so it is easy to pick up but still rewards skill.

> Developed by **Raj Baing** as a B.Sc. Computer Science project (University of Mumbai).

## Gameplay
1. Pick a car in the Showroom. Each car has stats for top speed, acceleration, handling and braking.
2. Choose a track, the number of laps and up to 4 AI opponents.
3. Race through checkpoints and laps using boost, handbrake drifting and a manual/automatic gearbox.
4. Finish the race to see the podium, your race time, average speed and XP.
5. XP is saved between sessions and unlocks badge tiers: **Rookie → Amateur → Pro → Elite → Legend**.

## Features

- **Vehicle physics:** WheelCollider car controller with boost (with cooldown), handbrake drifting and a manual/automatic gearbox
- **Adaptive AI:** AI cars adjust their speed based on the gap to the race leader and to the player, so races stay close
- **Smart cornering:** AI looks ahead to detect sharp turns, brakes based on the turn angle and boosts out of corners
- **Stuck recovery:** AI cars that get wedged reverse, try a forward escape and give up gracefully if needed
- **Race system:** Checkpoints, lap tracking, live position and final standings
- **Progression:** XP, levels and badge tiers saved locally with PlayerPrefs
- **UI:** Splash screen, showroom, in-race HUD (speedometer and minimap), pause menu, settings panel and podium screen
- **Demo mode:** Press `Shift + A` to switch your car between manual and automatic (self-driving) control during a race

## Controls

| Key | Action |
|-----|--------|
| `W` / `↑` | Accelerate |
| `S` / `↓` | Brake / Reverse |
| `A` / `D` or `←` / `→` | Steer |
| `Space` | Handbrake (drift) |
| `Left Shift` | Boost (recharges after use) |
| `Shift + A` | Toggle Manual / Automatic mode |
| `Esc` | Pause |



## Built With

- Unity 2022.3 LTS
- C#
- TextMeshPro, Unity UI, Unity Splines, Post-Processing Stack v2
- Visual Studio, Git and GitHub


## Project Structure

Scripts are grouped into Gameplay, AI, UI, Persistence, Audio/VFX and Scenes/Flow.

## Future Plans

- More tracks and cars, car customization and weather effects
- Cloud save and global leaderboards
- Online multiplayer
- Mobile and controller support

