# GorillaPhone

Hi, and welcome! GorillaPhone is a mod that gives you a phone in Gorilla Tag.

It's a real object in the world. You can grab it, hold it, drop it, and throw it, and it bounces off the floor and walls like you'd expect. The screen has a home screen, a camera, and a photo gallery, so you can take pictures of the places you visit and look at them without leaving the game.

This is an early version (0.1.0). Most of it has been played and tested in VR, but the Gallery and the Music sound test are new, so you might run into a rough edge or two. Thanks for giving it a try.

## What you need

- Gorilla Tag on PC (the Steam version) and a VR headset. It doesn't work on a Quest that isn't connected to a PC.
- Windows 10 or 11.
- BepInEx 5, the mod loader that most Gorilla Tag mods use. The install steps below show where to get it.

That's everything. GorillaPhone is a single file and doesn't need any other programs.

## How to download it

Open the [Releases page](https://github.com/itsreallyhex/GorillaPhone/releases), find the newest release, and under Assets click `GorillaPhone.dll` to download it. That one file is all you need.

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

There are three apps you can use right now: Camera, Gallery, and Music. Video is on the screen too, but it's greyed out and marked "soon". It's a placeholder for later.

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

For now, the Music app is a test of the phone's sound. It isn't in the current download yet, and it will come in a later update. Once you have it, poke the big play button and the phone starts playing a short tune on a loop. Poke it again to pause. The plus and minus buttons change the volume.

The sound really comes from the phone. It gets quieter as the phone gets farther from you, and it sounds muffled when a wall or the ground is between the phone and you. The screen says "muffled" when that happens, so you can see it working. Try dropping the phone behind a wall and walking around.

Only you can hear it. Playing music from your computer through the phone will come later.

## What you can do with it

- Take selfies of your gorilla and its cosmetics.
- Take pictures of the maps you visit, and come back later to see where you were and when.
- Zoom in for far-away shots, or pull back for wide ones.
- Throw the phone around and watch it bounce, or use it as a fidget toy.
- Look back through all your photos on the phone itself, without opening a folder on your computer.

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
- `Volume`, in the `[Audio]` section, sets how loud the phone's tune starts, from 0 to 1. You can also use the plus and minus buttons on the phone. (Only builds with the Music sound test have this.)
- `MuffleCutoff`, also in `[Audio]`, sets how muffled the tune gets behind a wall. A lower number is more muffled. (Only builds with the Music sound test have this too.)
- `Enabled` turns the phone off without removing the file.

## Good to know

The phone is just for you. It stays on your computer, doesn't use the internet, and other players can't see it. It's built so you can't stand on it or push off it, so it doesn't change how the game plays.

GorillaPhone is a fan-made mod. It isn't made or supported by Another Axiom, the studio behind Gorilla Tag. Using any mod is at your own risk, so please keep that in mind.

## If something isn't working

- No phone shows up: make sure the `BepInEx` folder exists (you get it by running the game once after installing BepInEx) and that `GorillaPhone.dll` is inside `BepInEx\plugins`. Then open `BepInEx\LogOutput.log` and look for lines that start with `[GorillaPhone]`. They usually say what went wrong.
- You can't find the phone: hold X and A together for about a second.
- The date shows little squares: the game's font doesn't have the letters of your language. Set `DateFormat` to `yyyy-MM-dd` and it will show numbers instead.
- A photo comes out upside down: open the settings file and change `PhotoFlip` from `Auto` to `Flip`. If it's still wrong, try `NoFlip`.
- The Gallery says there are no photos: take one in the Camera app first, or check that `PhotoFolder` points to where your photos are.
- A photo shows "can't open" in the Gallery: the Gallery reads photos taken by this mod. A picture that was edited or saved by another program might not open there.

If you find something else that's off, please open an issue on this page and tell me what happened. Screenshots and the `[GorillaPhone]` lines from `LogOutput.log` help a lot.

## Coming later

These are planned, but they aren't in this version, and I can't promise when they'll arrive:

- A Music app that shows what's playing on your computer and lets you pause, skip, and change the volume.
- A Video app for short form videos. It shows a website that plays them, running in a browser on your computer, right on the phone. You watch and swipe to scroll without leaving the game. This one is a big step, and I can't promise it will work.
- Music from your computer coming out of the phone. The phone's sound already gets quieter with distance and muffled behind walls (try it in the Music app), but for now it only plays a built-in tune.

## Thanks

Thanks for reading this far. I hope the phone makes your time in the trees a little more fun.
