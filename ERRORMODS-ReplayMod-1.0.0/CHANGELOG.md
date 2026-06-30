# 1.2.2
Hey so like apparently the whole mod was broken for the past two months so:
- Made replay clones not lag the game anymore and allowed replays to actually play correctly
- Fixed VFX not showing up in replays
- Fixed voices not working correctly on clones
- Added compression level setting for replay files
- Added a voice volume setting
- Renamed `Visuals Toggles` to `Playback Toggles`
- Fixed an issue with custom map recordings not changing the map when in the same scene
- Fixed boulders spawning in Pit replays
- Made saving recordings not play the success sound when an error happened
- Made sure everything was cleared between recordings
- Removed setting structureId for replay structures
- Made replay clones voices stop playing when switching replays
- Made sure pooled clones are reset correctly when switching replays
- Allowed VFX to update when skipping frames
- Added grounded friction VFX to the replays
- Fixed some parts of replay clones not updating according to the replay
- Made POV mode compatible with Custom Avatars
- Added the healthbar to be visible and working in POV mode
- Allowed more VFX types to be controlled by the replay
  - Explosions
  - Uppercut
  - Straight
  - Kick
- Fixed position and rotation recording still adding data when not needed
- Reduced the amount of arrays it has to make to serialize replay files
- Made sure voices lined up with when players actually spoke
- Added more safety checks around saving replay files
- Made sure external mods couldn't error internal systems with the API

# 1.2.1
- Actually added voice settings menu
- Allowed custom scene replays to be loaded if in recorded scene
- Fixed players sometimes being invisible because of null mesh.

# 1.2.0
- Added MIT License to the project
- Added Melon Color back (red)
- Made recordings only serialize after scene loads to prevent errors
- Made players not able to be null in infos anymore (hopefully)
- Added voice recording and playback pipeline (wip)
- Fixed corrupted files in the explorer making the game freak out and break
- Made favorite icon disappear correctly if it's a folder entry
- Added more transition states for recording icon
- Made recording icon more persistent over scene loads
- Fixed recorded replay clones not showing up in recordings
- Removed all ModUI and ModUI+ references
- Fixed crystal erroring when holding grip between scene loads
- Fixed replay explorer not letting you change replays when the current page is all folders

# 1.1.1
- Me when I forget to add the dependencies

# 1.1.0
- Replaced ModUI with UIFramework
- Added player pooling (improved performance and reduced lag) 


- ACTUALLY fixed images in readme this time
- Fixed links to thunderstore wiki in readme
- Changed timings for UI long press buttons.
- Made recordings end on scene async load instead of onMatchEnd for more seamless back-to-back playback.

# 1.0.1
- Moved replay tables to new "Multiplayer Maps in Singleplayer" button positions.
- Changed 'Exit Scene' button to 'Exit Map'
- Fixed playback controls opening in the wrong place when using the button.
- Fixed pink background on the player selector.
- Changed color of explorer path to be more visible.
- Made sure to put the images actually in the zip file
- Made sure the readme linked to the github/wiki pages instead.

- Fixed errors with DebugLog
- Fixed buttons not being visible sometimes.
- Fixed shiftstones disappearing when loading back from a replay.
- Fixed local healthbars appearing when a replay is loaded.
- Fixed fistbump coin pool being named wrong.


# 1.0.0
- Created

Need help?
Join the [Rumble Modding Discord](https://discord.gg/BeWpUXqjtH). People there are happy to help.