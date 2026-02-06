Maybe it was just me, but because Plex seemed pretty good at working out what show/season/episode something was just from the filename I got lazy and didn't really keep things very structured, so when I moved over to Jellyfin a lot of stuff broke; I did start reorganising the mess manually, but due to the aforementioned laziness the process stalled pretty quickly! I finally got fed up with half my library being "missing" so I decided to automate it and figured I'd share it in case there are others in the same boat.

The UI is a bit primitive, so here's how to use it in "TV" mode:

* Create a new destination folder for whatever series you're trying to tidy up (e.g. "Game of Thrones").
* Drag all the relevant loose files/folders containing the unsorted episodes into a temporary folder. (You can drag them into the destination folder and set source and destination the same, but using a temporary folder makes it easier to get rid of any accompanying cruft, empty folders, etc. afterwards.)
* Drag the destination folder onto the appropriate space in the app.
* Do the same with the source folder.
* Click "Search" - it'll find all the video files in the source folder and attempt to work out what season/episode it is; if it can't work it out it'll leave them blank and you can edit them. (It's worked with everything I've thrown at it so far but if anyone comes across a naming scheme that it doesn't recognise or I missed any filetypes let me know!) Update: S.E naming schemes don't work (e.g. "Awesome TV Show 3.6.mp4"); trying to find a reasonable solution that works without getting confused by stuff like "DTS 5.1"!
* Type the name of the series in the "Show name" box. This is now guessed from the destination folder name but can be edited if needed.
* Select whether to copy or move the files. (Moving is faster; if they're on different drives you can only copy them.)
* Hit Go
* PROFIT!

It'll create "Season XX" folders for each season, and stick the files in the relevant folder renaming them to ShowName SxxExx as per the naming guidelines in the Jellyfin docs. (It'll also move any matching .srt files.)

Movies functionality is in a *very* early state so rather than documenting it I'll just say "don't use it!"
