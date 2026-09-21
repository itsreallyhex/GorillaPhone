# GorillaPhone

Hi, and welcome! GorillaPhone is a mod that gives you a phone in Gorilla Tag.

It's a real object in the world. You can grab it, hold it, drop it, and throw it, and it bounces off the floor and walls like you'd expect. The screen has a home screen, a camera, a photo gallery, a sound test, and a Video app for short videos. So you can take pictures of the places you visit, look at them without leaving the game, and scroll through videos on the phone while you hang out in the trees.

This is an early version (0.1.0). Everything in this guide has been played and tested in VR, but the Music sound test and the Video app are the newest parts, so you might run into a rough edge or two. Thanks for giving it a try.

## What you need

- Gorilla Tag on PC (the Steam version) and a VR headset. It doesn't work on a Quest that isn't connected to a PC.
- Windows 10 or 11.
- BepInEx 5, the mod loader that most Gorilla Tag mods use. The install steps below show where to get it.
- For the Video app only: Microsoft Edge or Google Chrome, and an internet connection. Windows already comes with Edge. The rest of the phone needs neither.

GorillaPhone itself is a single file. The only other program it uses is the browser you already have.

## How to download it

Open the [Releases page](https://github.com/itsreallyhex/GorillaPhone/releases), find the newest release, and under Assets click `GorillaPhone.dll` to download it. That one file is all you need.

The Music sound test and the Video app were added after the first release, so they only show up in the download once a newer release is published. If you'd like them sooner, the source code on this page has them, and you can build it yourself.

## How to install it

1. If you don't have BepInEx yet, download BepInEx 5 (a 5.4 version, the 64-bit one) from the [BepInEx releases page](https://github.com/BepInEx/BepInEx/releases) and unzip it into your Gorilla Tag folder. In Steam, you can find that folder by right-clicking Gorilla Tag, choosing Manage, and then Browse local files. Start the game once and close it again, so BepInEx can set up its folders.
2. Copy `GorillaPhone.dll` into the `BepInEx\plugins` folder inside your Gorilla Tag folder.
3. Start the game and load in. A few seconds later, a phone appears in front of you and drops to the ground.

To remove the mod, delete `GorillaPhone.dll` from that folder.

## Using the phone

To pick it up, put your hand close to the phone (within about 15 centimetres) and squeeze the grip button. Let go to drop it, or swing your arm and let go to throw it. It goes as fast as your hand was moving. You can also pass it from one hand to the other by squeezing the grip of your other hand while it's close.

If the phone ends up somewhere you can't reach, hold the X and A buttons together for about a second and it comes back to you. (On Quest-style controllers, those are the lower face button on each controller.) If it falls far below you or gets very far away, it comes back by itself.

The screen turns on when you're holding the phone or looking at it from close by. When you leave it alone for a while, the screen goes dark, and the next time it wakes up it starts on the home screen.

### The home screen

You'll see the time, today's date, and your apps. The date comes from your computer, so it uses your own time zone and your usual date style.

There are four apps: Camera, Gallery, Music, and Video.

To open an app, poke its icon with the pointing finger of your free hand.

### The camera

The camera opens with a live view on the screen. It starts on the camera on the back of the phone, so it sees what's in front of the phone. Press the round arrow button to switch to the front camera for selfies. Selfies are mirrored, just like on a real phone.

To take a photo, poke the big round button, or squeeze the trigger of the hand that's holding the phone. That second way is handy when you're lining up a shot. You'll see a quick flash and hear a shutter click, and a small preview of your photo shows up in the bottom corner.

The plus and minus buttons on the right zoom in and out. The number between them shows how wide the view is, and a smaller number means more zoomed in. The button in the top corner switches between Low, Med, High, and Ultra. A higher setting gives you a sharper live view and a bigger photo. Ultra looks best, and it asks the most of your computer, so drop it down if the game starts to feel choppy.

Your photos are saved as PNG files in the `GorillaPhone` folder inside your Pictures folder. Zoom and quality go back to normal when you restart the game.

### The gallery

The Gallery shows all the photos in that folder, newest first, including ones you took before you had the Gallery.

- Scroll with the up and down arrows at the bottom, or push a finger into the photos and drag. The list keeps drifting for a moment after you let go, like a phone does.
- Tap a photo once to select it. Tap the same photo twice quickly to open it.
- When a photo is open, you can see when you took it, which camera you used, and which map you were on. Use the left and right arrows to move between photos, and the four-squares button to go back to the grid.
- To delete a photo, press the bin button. It asks you first. The photo isn't erased. It moves into a folder called `Deleted` inside your photo folder, so you can get it back, or empty that folder yourself when you're sure.

The map name comes from the game itself, and it's saved inside each photo you take from now on. Photos taken before that show "Unknown map". Sometimes the game has two areas active at once, so you might see something like "Forest + City". Custom maps just show as "Custom Map".

### The music app (a sound test for now)

For now, the Music app is a test of the phone's sound. It came after the first release, so the first download doesn't have it. Poke the big play button and the phone starts playing a short tune on a loop. Poke it again to pause. The plus and minus buttons change the volume.

The sound really comes from the phone. It gets quieter as the phone gets farther from you, and it sounds muffled when a wall or the ground is between the phone and you. The screen says "muffled" when that happens, so you can see it working. Try dropping the phone behind a wall and walking around.

Only you can hear it. Playing music from your computer through the phone will come later.

### The video app

The Video app shows short vertical videos, the kind you swipe through. It opens a website that plays them (a popular short video site by default, and you can change it), and you watch and swipe without leaving the game.

Here's how it works. The first time you open it, the phone starts a browser in its own window on your computer's desktop. That's Microsoft Edge, or Chrome if you don't have Edge. The phone shows what the browser shows, and your pokes and swipes go back to it. When you close the game, the browser closes too. It keeps its own profile in `%LOCALAPPDATA%\GorillaPhone\browser-profile`, separate from your everyday browser, so nothing from your normal browsing is mixed in.

- Tap where you would click. If you miss a small button by a little, the phone nudges your tap onto the nearest clickable spot, which helps with things like the close cross on a pop up.
- Flick up quickly for the next video, and flick down for the previous one.
- A slower drag scrolls the page, which is handy for comments and lists.
- The chip in the bottom left switches between Swipe and Drag. In Drag mode your finger works like a mouse, so you can use sliders and puzzle style checks.
- Back goes to the previous page and Reload refreshes it. The plus and minus buttons change the volume, the same volume as the Music app. The home button leaves the app and pauses the video.

The sound comes out of the phone, not your speakers. It gets quieter as the phone gets farther away, it sounds muffled behind a wall, and your computer stays silent. The video keeps playing when you put the phone down or walk away, so you can toss it across the room and hear it from there. It only pauses when you go back to the home screen.

You can watch without an account. Likes, comments and follows need one. There's no keyboard in VR, so log in once in the browser window on your desktop and it remembers you. Your login is saved in the profile folder above, so treat that folder like the saved logins in your own browser. Delete it to log out.

A few things to know. The Video app needs an internet connection, because the browser loads a real website. Sites sometimes show checks or puzzles, and you can solve those on the phone in Drag mode or in the desktop window. Websites also change over time, so it might stop working one day, and I can't promise it won't.

## What you can do with it

- Take selfies of your gorilla and its cosmetics.
- Take pictures of the maps you visit, and come back later to see where you were and when.
- Zoom in for far-away shots, or pull back for wide ones.
- Throw the phone around and watch it bounce, or use it as a fidget toy.
- Look back through all your photos on the phone itself, without opening a folder on your computer.
- Watch short videos on the phone and hear them come from it.

## Settings

The first time you run the mod, it creates a settings file at `BepInEx\config\com.gorillaphone.gorillaphone.cfg`. Open it in Notepad, change something, and save. Most changes take effect right away, without restarting the game.

A few you might want:

- `Width`, `Height`, and `Thickness` set the phone's size in metres. If it feels too big or too small in your hands, change these.
- `PhotoFolder` sets where photos are saved and where the Gallery looks. Leave it empty to use the `GorillaPhone` folder in Pictures.
- `DateFormat` changes how the date looks on the home screen. Leave it empty to use your computer's own style, or try something like `yyyy-MM-dd`.
- `ThrowMultiplier` makes throws weaker or stronger.
- `GrabRadius` sets how close your hand needs to be to grab the phone.
- `PokeReach` sets how far past your finger's last joint the phone feels a touch. If presses land beyond where your finger looks, lower it. If you have to push through the screen, raise it.
- `ShutterVolume` sets how loud the shutter click is.
- `Volume`, in the `[Audio]` section, sets how loud the phone's sound starts, from 0 to 1. You can also use the plus and minus buttons on the phone. (Only builds with the Music sound test have this.)
- `MuffleCutoff`, also in `[Audio]`, sets how muffled the sound gets behind a wall. A lower number is more muffled. (Only builds with the Music sound test have this too.)
- `Enabled` turns the phone off without removing the file.

These are in the `[Video]` section, and only builds with the Video app have them:

- `StartUrl` is the page the Video app opens. If you land on a grid of small pictures instead of a video, change this to the address of a page that plays videos one at a time.
- `PhoneSound` decides whether the video's sound comes out of the phone (the default) or stays on your computer's speakers.
- `AudioDelayMs` is how long the sound waits before playing, so it lines up with the picture. Raise it if the sound gets ahead of the picture or has small gaps.
- `TapAssist` sets how far a missed tap can jump to a nearby button, in pixels of the page. Set it to 0 to turn it off, and lower it if taps land on the wrong thing.
- `Fps` sets the most pictures per second the phone draws. Lower it if the game feels choppy.
- `ViewportWidth`, `FrameWidth` and `Mobile` change how big the page looks and whether the site shows its phone layout. These only apply the next time the browser starts, so restart the game after changing them.
- `BrowserPath` is the full path to `msedge.exe` or `chrome.exe`. Leave it empty and the phone finds one by itself.
- `Enabled` turns the Video app off.

## Good to know

The phone is just for you. Other players can't see it, and it doesn't send anything through the game's network. The one part that uses the internet is the Video app, because the browser it starts loads a website. Your photos never leave your computer. The phone is built so you can't stand on it or push off it, so it doesn't change how the game plays.

GorillaPhone is free software under the GNU General Public License, version 3. The full text is in the `LICENSE` file.

GorillaPhone is a fan-made mod. It isn't made or supported by Another Axiom, the studio behind Gorilla Tag. Using any mod is at your own risk, so please keep that in mind.

## If something isn't working

- No phone shows up: make sure the `BepInEx` folder exists (you get it by running the game once after installing BepInEx) and that `GorillaPhone.dll` is inside `BepInEx\plugins`. Then open `BepInEx\LogOutput.log` and look for lines that start with `[GorillaPhone]`. They usually say what went wrong.
- You can't find the phone: hold X and A together for about a second.
- The date shows little squares: the game's font doesn't have the letters of your language. Set `DateFormat` to `yyyy-MM-dd` and it will show numbers instead.
- A photo comes out upside down: open the settings file and change `PhotoFlip` from `Auto` to `Flip`. If it's still wrong, try `NoFlip`.
- The Gallery says there are no photos: take one in the Camera app first, or check that `PhotoFolder` points to where your photos are.
- A photo shows "can't open" in the Gallery: the Gallery reads photos taken by this mod. A picture that was edited or saved by another program might not open there.
- The Video app says it can't find a browser: install Microsoft Edge or Google Chrome, or put the path to it in `BrowserPath`.
- The Video app shows no picture: look at the browser window on your desktop. It might be waiting on a cookie message or a check, and you can answer it there. If the phone says the browser stopped, tap Reload and it starts again.
- The video has small gaps in the sound: raise `AudioDelayMs` a little, for example to 300.
- Taps in the Video app land on the wrong thing: lower `TapAssist`. If small buttons are hard to hit, raise it.
- A browser window is still open after the game crashed: the phone closes it the next time you start the game, or you can just close the window yourself.

If you find something else that's off, please open an issue on this page and tell me what happened. Screenshots and the `[GorillaPhone]` lines from `LogOutput.log` help a lot.

## Coming later

These are planned, but they aren't in this version, and I can't promise when they'll arrive:

- A Music app that shows what's playing on your computer and lets you pause, skip, and change the volume.
- An on-screen keyboard, so you can type in the Video app (to search, to comment, or to log in) without taking the headset off.
- Music from your computer coming out of the phone. The phone's sound already gets quieter with distance and muffled behind walls (try it in the Music and Video apps), but the Music app only plays a built-in tune for now.

## Thanks

Thanks for reading this far. I hope the phone makes your time in the trees a little more fun.
