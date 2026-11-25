# Changelog

(Numbering restarted at 0.0.1 after 0.9.9. Older builds are in old-builds.)

## 0.0.60
- New setting "5 - Enemies / Enemy count" (1x to 3x): more creatures from overworld spawns, raids, nests, camps and dungeons. Bosses and this mod's events are never multiplied.

## 0.0.59
- Gear drops stay in their biome. New setting "2 - Loot / Drops from the next biome" (off by default) brings back the small chance (loot.yml nextBiomeChance, 3%) of a drop from the next biome up.
- Feasts without Eitr now give some: Meadows 10, Black Forest 13, Swamp 15, Mountain 18, Sailor's Bounty 20, Plains 20 (loot.yml feastEitr), for gear mods that bring magic before the Mistlands.
- Event reward chests: a pulsing ping on your map (and minimap) marks each of your reward chests until you empty it. Kept through a relog. lla-chestpings [clear] lists or clears them.

## 0.0.58
- Quest boards: quests you're on hold their slot. A new offer appears only when you turn one in or abandon one (it used to appear the moment you accepted).
- Bog Witch: "Bring me neck tails" is now "Bring me a neck's head": 1 Neck trophy (they were what she took anyway).
- Raven's Wing: enemies no longer target the ravens, so they can't tank for you.
- Boogie and Grillo play a voice line 25% of the time instead of every time (vendors.yml voiceChance).
- README images readable on dark pages.

## 0.0.57
- Boogie's Bar sells the Serving Tray (250g) once Eikthyr is beaten, and each feast once its boss is beaten: Meadows 250, Black Forest 300, Swamp 350, Mountain 400, Plains 450, Sailor's Bounty 450 (after a Sea Serpent), Mistlands 500, Ashlands 600, Deep North 700. Set in vendors.yml (stalls: items).
- Stall prices round halves up (a 22.5g bomb is now 23g).
- Night: from dusk to dawn creatures deal 30% more damage, have 30% more health and drop better gear (each tier step 1.5x as likely; Unique chance x1.5). A message says when night falls and when dawn breaks. Bosses, tamed creatures and summons are left alone. Settings in enemies.yml (night).

## 0.0.56
- New event: Hunted. Out in the world (never at a base) something may come for you: a pack of 2-5 of the biome's creatures, or a lone Apex Predator with +2 stars, 2x the chance to drop gear and 3x the Unique chance (sometimes from the next biome). Kill them all for a reward chest. There's no timer on screen. Settings in events.yml (hunted).
- World events now appear 30% closer: 105-420 m from you (was 150-600 m). If you edited events.yml in your config folder, change spawnMin/spawnMax yourself.
- Debug: lla-hunted [apex|normal].

## 0.0.55
- New beta icon and README wording. No gameplay changes.

## 0.0.54
- Unique chances halved across the board (loot.yml uniqueChanceScale: 0.5): normal drops, stars, Unique Find, caps, unique enemies, bosses and event chests.
- New config slider "2 - Loot / Unique drop rate": 1x to 5x on top of that.
- Loot: an item is never below its crafting station's biome (loot.yml stationBiomes). The Arbalest (made of early materials at the Black Forge) is now a Mistlands item, so it can't drop in the Black Forest anymore; the earliest is the Plains, through the small next-biome chance.

## 0.0.53
- First test release. Tested: Galdr Focus, and affixes surviving upgrades at the Forge of Potential.

## 0.0.52
- Fix: the mod failed to load in 0.0.51 (a setting name had an apostrophe). The setting is now "3 - Visuals / Always use mod keyboard".

## 0.0.51
- The mod's own on-screen keyboard for controllers: pressing the right stick on the recipe search opens a key grid moved with the D-pad or left stick (key to key, hold to repeat). A types, X deletes, Y adds a space, B closes. While it's open the inventory's own controller input waits. In Steam Big Picture / SteamOS, Steam's keyboard is used instead unless "3 - Visuals / Always use mod keyboard" is on.

## 0.0.50
- Recipe search: a search box above the crafting list; type part of an item's name to filter the recipes. Controller: press the right stick to type (Steam's on-screen keyboard opens in Big Picture / SteamOS). Typing doesn't trigger game keys; the search clears when the inventory closes. Config: 3 - Visuals / Recipe search.
- One-Eyed King: only one champion at a time; casting again dismisses the old one as the new one rises.

## 0.0.49
- New Unique: One-Eyed King (Dead Raiser only). The staff raises a single skeleton champion instead of several: double health, +150% damage, 50% larger, named in gold. Casting again replaces it. Ordinary Dead Raisers are unchanged. Numbers in uniques.yml (values: health, damage, size).
- Uniques can be limited to specific items (uniques.yml items: [prefab names]).

## 0.0.48
- Defend: players (and their tamed creatures) can't damage the Shrine of Protection.

## 0.0.47
- Defend: the Shrine of Protection's health is set per biome (events.yml Defend shrineHealth): Meadows 300, Black Forest 600, Swamp 1000, Mountains 1500, Plains 2200, Mistlands 3000, Ashlands 4500, Deep North 5500.

## 0.0.46
- Defend: a health bar floats above the Shrine of Protection (blue, turning red as it drops), and every player's game treats it the same way (its max health, and no ward powers).

## 0.0.45
- Defend: raiders ignore players and go for the Shrine of Protection, even after being hit (until it's destroyed).

## 0.0.44
- World events, part 2:
  - Shrine: a glowing runestone in its buff's color (red Shrine of Fury +20% damage, blue Shrine of Swiftness +20% movement speed, gold Shrine of Fortune +5% Unique Find), with a pulsing light and drifting effects. "Touch the shrine" for 5 minutes of the buff (with a HUD icon); then it fades. No chest; the first player to touch it uses it up.
  - Defend: protect the Shrine of Protection (a breakable copy of the ward, with no ward powers) from three waves of raiders. It has 600 health in the Meadows, +50% per biome after; warnings at half and a quarter. Win: reward chests. Fail: the shrine is destroyed.
- Event weights: Horde 2, Champion 2, Ambush 2, Shrine 2, Defend 1, Boss 1 (the Boss stays 1 in 10).

## 0.0.43
- Unique slot background toned down to dark burnished gold: a deep brown middle with a narrow pale-gold edge glow lit from the top-left and faint brushed streaks. Sparkles and glint unchanged.

## 0.0.42
- The big map shows a countdown to the next world-event check ("New events in 7:32").

## 0.0.41
- Unique items: a polished gold-foil slot background (diagonal metal gradient, highlight, brushed grain, bevelled edge) with sparkles that twinkle in turn; the 20-second glint stays.
- Unique tooltips: an ornate gold frame (double border, diamond corners with a gem, crests at the top and bottom).

## 0.0.40
- World events are topped up every 10 minutes (was 2).
- The map shows only a blue circle for each event (no name, no raid-red): you find out what it is when you walk in.
- Reward chests spawn at the middle of the map circle.
- New rare event, Boss (1 in 10): the boss of the event's biome or of one up to two biomes lower, at 50% health and 75% damage, with the usual boss affixes and boss loot. Its kill doesn't count as defeating the real boss. Its reward chest: 3 boss-quality gear rolls with a 20% base Unique chance each.

## 0.0.39
- World events, part 1 (events.yml): the server keeps 2 events near each player, 150-600 m away, on land, in biomes you've reached (one past your last boss), never next to a base. Each has a circle and a name on everyone's map. Unfinished events fade after 60 minutes; the spot rests 45 minutes. Cap: 4 per online player, 16 total.
  - Horde: three waves of the biome's creatures. Champion Hunt: a unique enemy with escorts. Ambush: springs a wave when you step into it.
  - Win: each player who took part gets a personal chest (2 gear rolls from the event's biome at the highest star level defeated, plus 10 x the biome's sell multiplier in gold). Others can't open it; it vanishes once emptied.
  - Fail: nobody near it for 90 seconds, or 20 minutes without finishing.
- New command: lla-event lists events; lla-event Horde|Champion|Ambush places one 30 m ahead.

## 0.0.38
- Delve chests in cramped tombs: the floor is found by looking down from just above the spot (it used to start above low ceilings and land on top of them), and the spot needs headroom and a chest-sized space clear of walls.

## 0.0.37
- Delve chest depth is measured from the dungeon's entrance room, not from where you stand. lla-delve also reports whether the entrance was found and how many spots had no floor.

## 0.0.36
- Delves only use the dungeon you're in: nearby dungeons' interiors are also loaded, and their rooms and chests were being counted (the chest could land in a different crypt). Refilling creatures uses the same check.

## 0.0.35
- Delve chests: made switched off with their random-spawn part removed, so they can't hide themselves; placed at the deepest room, creature spawner or chest with floor under it. Each delve step is written to the log.
- New command: lla-delve (inside a dungeon) places a test chest and reports what it found.

## 0.0.34
- Delves into a dungeon you've already cleared bring its dead creatures back (spawners whose creature is dead may spawn again; ones still alive and ones near the entrance are left alone). quests.yml refillDelves.

## 0.0.33
- Delves place their own quest chest on the floor of the room deepest from where you come in (quests.yml delves: chest), instead of relying on a dungeon chest that might not exist. The old pick-a-chest is the fallback.

## 0.0.32
- Delves and camp bounties: the host (or single player) now searches the world's dungeon list directly, unexplored ones included, and names can match in part ("Crypt" finds Crypt2-4). Clients in multiplayer still ask the server.
- New command: lla-locations [text] lists the world's location names (e.g. lla-locations crypt).

## 0.0.31
- Quests reach one biome past your last boss (Black Forest after Eikthyr, Swamp after the Elder...), so Hildir's delves show up once Eikthyr is down. The shop's gear still stops at the boss's own biome.
- A vendor with no quests yet says so under the tabs instead of showing an empty list.

## 0.0.30
- Multiplayer: creatures (bosses and unique enemies too) get +30% health for each additional player within 50 m. Their health keeps its share when players come and go. Config: 5 - Enemies / Health per extra player, Extra player range (synced).

## 0.0.29
- Boss health bar shows the health as numbers (current / max). Config: 3 - Visuals / Boss health numbers.

## 0.0.28
- Twin bosses: both twins have 50% of the starred boss's max health and each deals 55% of its damage (bosses.twinHealth / twinDamage).

## 0.0.27
- Boss stars: +10% health and +5% damage each (was 15% / 10%).
- Twin bosses: the original stays full strength; the spawned copy has 25% of the boss's max health and deals 30% of its damage.

## 0.0.26
- Bosses: 0-2 stars (bosses.maxStars, up to 5), one boss affix each, plus one normal effect or infusion per star. Each star adds +10% health and +5% damage (bosses.healthPerStar / damagePerStar) and raises the Unique chance. The name shows the affixes and stars.
  - Summoner bosses call their own adds (bosses.adds, e.g. Greydwarf Elites for the Elder); Splitting never rolls on bosses; Black Hole never on Moder (bosses.excludedAffixes).
  - Twin bosses: the original stays full strength; the copy has 25% of its max health and deals 30% of its damage (bosses.twinHealth / twinDamage). Gear drops once.
- Infusions no longer weaken a creature's own stronger resistance (e.g. a Fire-infused boss that was immune to fire stays immune).
- New command: lla-boss <boss> [stars] [affix...].

## 0.0.25
- Unique enemies repeat fx_Potion_fireresist every 2 seconds, following them, with no sound (enemies.yml uniques.aura).
- Auras are now stripped before they wake up, so no sound plays at all (infusion auras too). Auras can follow the enemy (follow) and sit higher (height).

## 0.0.24
- Unique enemies: your tile is now drawn as a glowing layer over the body with one of the game's particle shaders (the metal shader's glow and texture features turned out to be missing from the game). Only the tile's bright lines glow; less mirror-like gold underneath. New look settings: overlay, overlayStrength, overlayCutoff. The log says which shaders it found.

## 0.0.23
- Unique enemies can wear your own seamless tile: textures/uniquecoat.png scrolls over them on a loop, tinted gold (or its own colors) with its bright parts glowing. Without the file, the shimmer bands stay. Settings in enemies.yml uniques.look: texture, textureGold, textureTiling, textureGlow, scrollX, scrollY.

## 0.0.22
- Unique enemies: polished gold instead of a near-mirror (Valheim's blurry reflections made it look olive and blocky), surface detail smoothed out, and bright gold shimmer bands sliding over them. New look settings: keepDetail, shimmer, shimmerSpeed, shimmerBands.

## 0.0.21
- Unique enemies are now liquid gold metal (Standard metal shader, reflecting their surroundings) instead of a gold tint. enemies.yml uniques.look: style (metal or tint), keepTexture, color, metallic, smoothness, emission.

## 0.0.20
- The unique-enemy command is now lla-uniqueenemy (in 0.0.18-0.0.19 it was lla-unique and hid the Unique item command).
- README: enemy testing commands, and compatibility notes (enemy mods, mods that conflict).

## 0.0.19
- Unique enemies wear a gold, metallic coat (creature and worn gear), with a faint gold self-glow. Tunable in enemies.yml uniques.look.

## 0.0.18
- Unique enemies: rare elite creatures with a gold name and gold glow. At least 2 stars (capped by biome), one boss affix plus one normal effect or infusion.
  - Overworld: one every 20-40 minutes you spend outside bases and dungeons; the next natural spawn within 80 m becomes it.
  - Dungeons and caves: 1-3 per dungeon. Once killed, they stay dead.
  - Loot: 2 guaranteed drops, 2x Unique chance, +1 star.
- Boss affixes (unique enemies now, bosses later): Reflective, Mending, Summoner, Elementalist, Enraged, Twin, Black Hole.
- New command: lla-uniqueenemy <creature> [stars] [boss affix] [affix], and lla-uniqueenemy timer [seconds].

## 0.0.17
- Sapping now drains 30% of max eitr per hit (was 20%).

## 0.0.16
- New enemy effect: Leeching (heals itself for 20% of the damage it deals you; any creature with 1+ stars).

## 0.0.15
- Auras for every infusion: Frost (vfx_dragon_ice_hit, 5 s, half size), Poison (vfx_blob_attack, 5 s, half size), Lightning (fx_lightningweapon_hit, 3 s), Spirit (vfx_Potion_stamina_medium, 4 s), Chaos (a random other aura every 2 s, replacing the last). Each aura keeps its own timing.
- New enemy effects: Exhausting (hits drain 15% of max stamina, anywhere) and Sapping (hits drain 20% of max eitr, Mistlands and later). Effects can be limited by biome with minBiome.

## 0.0.14
- Infusion auras at the enemy's feet (enemies.yml "auras"): Fire-infused repeats fx_fireskeleton_nova every 0.5 s at half size (it grows with the enemy). Visual only: no damage, no sound. The faint body effects are gone; the soft glow stays.

## 0.0.13
- lla-enemy takes numbers in order (stars, how many, random affixes) and words in any order (the creature and named affixes):
  lla-enemy 4 3 greydwarf fire-infused, lla-enemy 4 3 2 greydwarf.

## 0.0.12
- Frost-infused hits chill you for 3 seconds (frostSeconds).
- 3+ star enemies wear the vanilla 2-star look (color shift and extra parts), on top of their bigger size.
- Infused enemies look the part: their element's own effect loops on them (flames, frost, sparks, spirit wisps, poison bubbles) with a soft glow in its color; Chaos glows through every color.

## 0.0.11
- Fixed: most creatures (Greydwarfs, Draugr, Fulings, Necks... anything humanoid) skipped the star roll and affixes, since their startup skips the one the mod hooked.
- Affix names show in orange in front of the name on the enemy's health bar ("Quick Neck").
- Size grows from 3 stars only: 3 stars 30% bigger, 4 stars 40%, 5 stars 50% (sizeFromStars).

## 0.0.10
- Rending's bleed can kill. It goes straight to health: armor, blocking and shields (Staff of Protection) don't stop it, healing does.

## 0.0.9 - Enemy scaling (part 1)
- New data file enemies.yml. Every creature rolls its stars when it spawns (replacing vanilla's 0-2): 20% per extra star, +2% per boss defeated, capped by biome (Meadows 2, Black Forest 3, Swamp 4, then 5). Health and damage per star follow vanilla; size +10% per star.
- 1+ star creatures get one effect: Quick, Aggressive, Curious, Splitting, Armored, Rending, Deadly. From 3 stars, a 50% chance of an infusion: Fire, Frost, Lightning, Spirit, Poison, Chaos (4+). Shown as a name prefix ("Quick Greydwarf").
- Affixed enemies roll loot as one star higher; Splitting's copies drop no gear. Bosses, tamed creatures, summons keep their levels; bounty creatures keep their stars and roll affixes.
- Debug command lla-enemy <creature> <stars> [affixes].

## 0.0.8
- Stall build-menu icons from textures/boogiebar.png and textures/grillos.png ("iconFile" per stall).

## 0.0.7
- Stalls show in the build menu's Misc. and Decor tabs (the menu's tabs come from piece usage tags; "tags" per stall in vendors.yml).

## 0.0.6
- Fixed an error opening a stall's shop (its goods had no tooltip text set).

## 0.0.5
- Stalls go in the hammer's Decoration (Furniture) tab; "category" per stall in vendors.yml. Debug logging says whether each stall is buildable and which tab it's in.
- Grillo's circlet sits 0.2 m higher.

## 0.0.4
- Stalls are listed in the hammer's Misc tab (a build-menu mod had left them only under "Show All").
- Grillo is 30% bigger (npcScale), his circlet faces the right way and grows with him, and its glow is half as bright (hatLight).

## 0.0.3
- Boogie stands further forward, centered behind the counter. Grillo stands where the placeholder torch was (torch removed).
- Grillo's circlet: true size and facing forward (same fix as Boogie's hat; both hats now placed relative to the body).
- Vendor voice lines play without the game's reverb.

## 0.0.2
- Boogie's hat: true size and turned to face forward (it took the head bone's scale before). Hat position and rotation are now relative to the vendor's body.
- Fixed errors when starting to trade at a stall.
- Removed the placeholder torch from the middle of Boogie's Bar.

## 0.0.1 - Vendors, part 4: stalls
- Two buildable stalls in the hammer's Misc tab (vendors.yml "stalls"):
  - Boogie's Bar (20 Wood, 10 Leather scraps, 5 Deer hide, 10 Resin): a Greyling in a fishing hat sells meads and dishes.
  - Grillo's General Store (20 Corewood, 10 Fine wood, 10 Iron nails, 20 Entrails): a Fuling in a Dverger circlet sells arrows, bolts, bombs and ballista ammo.
- Each sells only what you've discovered how to make, priced by the item's biome (meads 10x, dishes 5x, 20 arrows/bolts 10x, bombs 15x, ballista ammo 20x the buy multiplier), and buys mod gear through Sell gear.
- The vendor turns to face you, greets you with a voice line (audio/boogie*.ogg, audio/grillo*.ogg), welcomes you the first time you trade, and chats now and then.
- The buildings come from the blueprints; fires are lit and item stands keep their items. A .blueprint in the config folder with the same name replaces one.

## 0.9.9
- Enemy health bars show 3 or more stars (vanilla only has art for 1 and 2, and showed none above that, so 3-star bounty targets looked like 0 stars).
- Debug log names each bounty creature's difficulty and stars as it appears.

## 0.9.8
- A finished bounty or delve takes its map pin and circle away right away, before you turn it in.

## 0.9.7
- Quest map pins are removed properly on claim, abandon and reset, even with the map closed. Reset my quests also clears pins left behind by earlier builds.

## 0.9.6
- Config "4 - Vendors / Reset my quests": drops your active quests and pins and brings back today's offers. Unticks itself.
- Fixed an error when switching tabs onto a shorter or empty list.

## 0.9.5
- Bounties get a circle on the map around their pin (areaRadius, 60 m). Step inside and the bounty's creatures come for you (animals that only flee, like deer, are left alone).
- Bounty creatures appear a little earlier, before you reach the circle.

## 0.9.4
- Bounties come in Normal (0-1 star), Hard (1-3 stars) and Very Hard (3+ stars). Haldor lists each of his 3 bounties in all three; taking one takes that bounty off the board.
- Harder bounties pay more: coins x1 / x1.5 / x2.5; Hard adds the boss tier bump chance to the weapon, Very Hard also raises it a tier. All in quests.yml (difficulties).

## 0.9.3 - Vendors, part 3: quests
- QUESTS tab is live (quests.yml). Each vendor offers 3 a day from your boss biome or lower; up to 3 active per vendor.
  - Haldor - BOUNTY: hunt a starred creature (circled on the map; it appears when you get close), or clear a camp (defeat every foe there).
  - Hildir - DELVE: find the glowing chest inside a marked dungeon.
  - Bog Witch - QUEST: bring trophies.
- Your quests are listed first (ACTIVE / DONE). The button reads Accept, Claim, or Abandon (press twice).
- Rewards: coins (60 / 60 / 40 x the buy multiplier of the quest's biome) plus a guaranteed weapon (with the boss tier bump), armor piece, or staff/trinket.

## 0.9.2
- Shop rows widen with the window (store items and quests).
- The vanilla sell-treasure button moves out with the wider window instead of hiding behind it.
- 10 px more room above the tabs. Accept stays Accept on the quest tab.

## 0.9.1
- Shop window 20% wider, so the tabs and the buttons below fit inside it and quest titles have room.
- Tabs no longer show the Buy button's controller prompt (the triggers switch tabs).

## 0.9.0
- Tabs are boxes copied from the shop's Buy button: the open tab looks hovered, with bright text; the other is plain and dimmed.
- The vanilla STORE title is hidden for good (its translation kept putting it back).
- Quest rows: title and description start where the item picture was and use the full row width.

## 0.8.9 - Vendor tabs (preview)
- Haldor, Hildir and the Bog Witch show STORE and QUESTS tabs in place of the title. Click them, or Left/Right Trigger on a controller.
- QUESTS lists sample entries (BOUNTY / DELVE / QUEST, a title and a short description) to check the layout; Buy reads Accept. Real quests come next.

## 0.8.8
- Controller: Sell gear is Left Bumper and Reroll is Right Bumper, keeping the triggers free for vendor tabs.

## 0.8.7
- Mystery item: after you buy it, it stays in the list greyed out (Sold out, with a countdown) and comes back 24 real hours later, per vendor.
- Sell gear and Reroll icons: find the copied coin picture properly, so sellgear.png and dice.png replace it.
- More space between STORE and the "Discover new lands..." line.

## 0.8.6
- Shops only offer items whose recipe you've discovered (no spears before you've picked up flint). Applies to daily specials, tier offers, Reroll types, the Unique gamble and the Mystery item. The Mystery item's 2% next-biome roll can be anything from that biome.

## 0.8.5
- Gear vendors show "Discover new lands and recipes so I can offer new item types!" under the shop title.

## 0.8.4
- Shop gear only comes from your boss biome or lower. Reroll offers every type that exists by then (no swords in the Meadows).
- The Mystery item has a 2% chance to come from the next biome up (mysteryNextBiomeChance).

## 0.8.2
- Shop buttons: Sell gear shows textures/sellgear.png in place of the coin (the coin stays if the file is missing). Reroll shows the dice and its price, no coin.
- While a shop is open, Keen Combat's trigger skill and held heavy attack are paused, so Right Trigger only rerolls.

## 0.8.1 - Shop simplified
- Each gear shop now shows: 2 daily specials, a Mystery item (once a day, any type, its name in an animated tier-color gradient), and Uncommon to Legendary offers of today's item type (plus the Unique gamble after the Ashlands boss).
- The Type button is now Reroll (Right Trigger): swaps today's item type for 50 coins, then 60, 80, 110, 150... Resets to 50 every in-game day, per vendor. Shop prices are unchanged by rerolling; the +50% type markup is gone.
- Custom icons: textures/mystery.png (Mystery item) and textures/dice.png (Reroll button).

## 0.8.0 - Vendors, part 2: buying gear
- Haldor (weapons), Hildir (armor, capes, shields) and the Bog Witch (staffs, trinkets) sell mod gear at the top of their shop list:
  - 2 daily specials each: pre-rolled items you can inspect, the same for everyone, new every in-game day (10 x tier price).
  - A mystery item: tier odds from the last boss's biome, 1% Unique chance (40).
  - A random item of each tier, Uncommon to Legendary (4 x tier price).
  - The Unique gamble once the Ashlands boss is down (2,500).
  - All prices x the buy multiplier of the last boss's biome; gear comes from that biome.
- New "Type" button under those shops (Right Trigger on controller) picks the item type for +50%.
- vendors.yml: shops, type names, boss keys and every price.

## 0.7.1
- Sell gear button is on Left Trigger for controllers (Y always opens the inventory instead).
- Windforce: every arrow pierces, not just the 20% spread.

## 0.7.0 - Vendors, part 1: coins and selling
- Coins weigh nothing and stack to 999,999. Slots show short amounts (12.5k, 1.25M); the tooltip shows the exact count.
- Mod gear tooltips show what it sells for: tier price x the item's biome multiplier (crafted gear sells for 25%).
- Every vendor's shop has a "Sell gear" button. It opens a sell box beside your inventory: drop gear in and press "Sell: N" (the chest's Take all button). Legendary and Unique gear asks for a second press. Equipped gear is refused.
- Unsold gear goes back to your bags when the box closes (or drops at your feet if they're full). Walking away from the vendor closes it.
- New data file vendors.yml: coin stack, sell prices, crafted share, confirm tiers, buy multipliers.

## 0.6.5
- Twin Embers reworked: each cast fires 2 extra embers in a narrow fan beside the original (30% damage each), instead of splitting on impact.
- extraProjectiles takes percent (damage of the extra projectiles).

## 0.6.4
- Hel's Harvest switched on: kills rise as thralls (Charred Twitchers) where the enemy fell, fighting for you for 30 seconds or until they fall, up to 2 at once; two raises, then a 3-minute wait.
- Powers can have charges: uses before the cooldown starts.

## 0.6.3
- Rooted Revenge: every 1 to 2 minutes in a fight, roots last 10 seconds, +20% damage to rooted enemies; vfx_beechlog_half_destroyed and sfx_staff_elder_root_swing. Debug logging logs each root.
- Kinetic Quake: vfx_RockDestroyed_large and sfx_rock_wall_crumble. Blasts (nova) take a sound, show for everyone nearby, and are logged with Debug logging.
- Raven Mantle: a stopped projectile shows vfx_seagull_death and plays sfx_archery_target_hit, for everyone nearby. Cue sounds can be the game's own sfx_ prefabs.
- Mistcloak: vfx_blackice_destroyed bursts on you and mistcloaksound.ogg plays when it procs, seen and heard by everyone nearby.

## 0.6.2
- Galdr Focus rolls for every cast that goes off (each burst of a held staff included), and Debug logging shows each refund.
- Fixed: Twin Embers' fragments flew off into the sky; they now arc out a few metres and land around the impact.
- Bifrost Eye: 50% chance when hit, once every 30 seconds.
- Fixed: piercing projectiles were deflected by the first enemy (bolts especially). They now fly on unchanged. Pierce max counts enemies passed through: Frozen Fjord hits up to 3, Wolf's Fang up to 4.
- Fixed: Wolf's Fang spears dropped at the first enemy hit. A piercing spear now flies on, and drops (or returns) where it finally stops.
- Throat-Cutter's burst uses the game's vfx_BloodDeath at 130% size and plays throatcuttersound.ogg for everyone nearby.
- fx can name any of the game's effect prefabs (vfx_..., fx_...).

## 0.6.1
- Character Sheet: an Active effects section lists running buffs (e.g. the dodge damage boost) with seconds left, the circle of power, and Raven Mantle's feathers. The sheet refreshes itself while open.

## 0.6.0
- 15 more Uniques switched on: Throat-Cutter, Life Siphon, Wolf's Fang, Frozen Fjord, Bifrost Eye, Twin Embers, Galdr Focus, Mistcloak, Raven Mantle, Kinetic Quake, Path of the Righteous, Rooted Revenge, Bog Walkers, Greed's Creed, Adrenal Catalyst.
- New trigger onAttack. New effects: adrenaline, storeBurst, drain, blink, refundCost, split, vanish, feathers, shield, root, coinDamage; pierce takes max, falloff and staminaPerHit; nova takes at: ahead and percentOfBlockPower. New condition targetRooted.
- Their descriptions show the real numbers.

## 0.5.5
- Windfury's whirlwind rebuilt from spinning glowing arcs around you (the same kind of mesh as the slash), plus glints at your feet.

## 0.5.4
- Fixed: Windfury's whirlwind only spawned at your feet; its streaks now fill the whole column around you, brighter and denser.
- The log names each sound file it loads, with its length.

## 0.5.3
- Thrasher and Windfury reworked: no sped-up extra swing. When they proc, the hit lands again instantly, 1 more time (Thrasher) or 2 more times (Windfury), a few hundredths of a second apart, so the damage numbers pop up side by side. Each extra hit is a full hit (it can crit, heal you, and so on).
- Fixed: conditions like "targetStaggered: true" filled the log with "can't read the number 'true'" warnings.
- Debug logging names each proc effect and its sound.

## 0.5.2
- Windfury: a whirlwind swirls up around you when it procs (cyan streaks, wider dashes, glints at your feet), and windfurysound.ogg plays.

## 0.5.1
- Fixed: Thrasher's and Windfury's slash didn't show. It's now built like the light shafts (fade baked into its texture, color through the material), and a bit bigger and brighter.

## 0.5.0
- Extra attacks: new effect extraAttack. The character really swings again right after the current swing, at 2.5x speed and free of stamina, with a crescent slash effect and a sound for everyone nearby.
- Switched on: Thrasher (1 extra swing, blue slash, thrashersound.ogg) and Windfury (2 extra swings, pale wind slash, windfurysound.ogg if present).
- New trigger onDodge (an attack would have hit you, but you dodged through it). Switched on: Dodge Damage Buff.

## 0.4.7
- Icy Protection: 50% less damage taken (was 90%).

## 0.4.6
- Icy Protection: every 1 to 2 minutes in a fight.
- Summons with nothing to fight go after the nearest enemy within 30 m (drakes used to just follow you).

## 0.4.5
- Fixed: a tooltip right at the scrolling limit (e.g. Pack Guardian) could flip in and out of scrolling every frame and end up with its title at the bottom.
- Draugr Oath: 10% chance on hit (was on kill); the Draugr rises beside the enemy you hit.

## 0.4.4
- Ravens face forward in flight.
- Ravens arrive with one caw (ravenattack.ogg) instead of a bat screech; their bat alert and idle sounds are muted.
- Summons no longer play the "tamed" effect when they appear.

## 0.4.3
- Raven's Wing: 3 ravens for 30 seconds, each strike 12% of your weapon damage.
- Ravens face the way they fly (bats back off tail-first).
- Ravens caw: ravenattack.ogg (in the audio folder) replaces the bat's attack sound, heard by everyone nearby.

## 0.4.2
- Raven's Wing: once every 60 seconds.

## 0.4.1
- Raven's Wing's raven: a bat (keeps its flying and attacks) wearing a black seagull's body, on every player's game. New summon option look: raven.

## 0.4.0
- New summon effect: friendly creatures from powers.yml (count, duration, how many at once, beside you / at the target / where a creature died, invulnerable, damage as a share of your weapon). "A|B" picks the first creature that exists, so another mod's creature can be used when it's installed.
- New conditions: everyN (every Nth trigger, e.g. every 5th block) and inCombat. "every" accepts a random range (every: 180-210) and waits for its conditions before firing.
- Switched on: Raven's Wing (a bat stands in for the raven), Draugr Oath, Pack Guardian, Icy Protection.
- SOMEDAY.md: a list of things we've decided to leave for later.

## 0.3.10
- Fixed: weapon-only Uniques and affixes (e.g. sword-only Frostgrip and Jarl-Slayer) could roll on armor, because armor's unused weapon-skill field defaults to Swords.

## 0.3.9
- Groundbreaker: once every 15 seconds.

## 0.3.8
- Fixed: Frostgrip's ring drew streaks over the player (rings now lie flat on the ground; Seidr Ring too).
- Frostgrip's ring is thinner, with wisps of cold mist flowing around it.

## 0.3.7
- Thunderstroke: a returning spear keeps trying to go back into your hands for a few seconds (the game won't equip mid-attack, mid-dodge or in the air). A broken spear says so instead.

## 0.3.6
- Testing commands take a repeat count as the first word (lla-drop 20, lla-tier 10 rare), up to 100, with one summary line.
- Fixed: spears never dropped or rolled affixes (their throw was mistaken for a consumable).
- lla-unique follows the same biome rule as real drops (e.g. Thunderstroke only on Plains-or-later spears and atgeirs).
- Seidr Ring's circle is a thin line of flame: a hot core with flickering tongues racing around it.
- Frostgrip's ring has a frost texture: a broken, uneven band with brighter ice flecks that slowly drifts.

## 0.3.5
- Summons: a friendly-summon system ported from Keen Combat's Primal Rally (friendly everywhere, never hurts players, buildings or crops, no loot, despawns on time or logout).
- Blobber Lobber now lobs a blob that fights for you for 20 seconds (up to 2 at once).
- Frostgrip: enemies in the aura visibly move in slow motion, and a faint cold 5 m ring follows you.
- Groundbreaker: once every 10 seconds.
- Seidr Ring's circle is red. Rune of Seasons shows faint motes in the current element's color.
- Thunderstroke's text: "Spears thrown return to your hand."
- lla-unique picks a random item that fits, or one whose name contains a second word (lla-unique thunderstroke spear).
- The Crown of Valheim and wooden weapons no longer drop (they can still be crafted and roll affixes).

## 0.3.4
- New effects: nova, strike, chainLightning, lob, slowNearby, zone, seasons, elementalDamage, recall. New conditions: comboHit, thrown.
- Switched on: Chain Lightning, Thunderstroke (with returning throw), Stormstomp, Groundbreaker, Storm Wall, Blobber Lobber, Frostgrip, Rotcrown, Seidr Ring, Rune of Seasons.
- Effects borrow Valheim's own visuals (Eikthyr's stomp, Dundr's lightning, the Ooze bomb); chain lightning draws bolts between enemies; Seidr Ring draws its circle on the ground.
- Character Sheet shows Rune of Seasons' current element.

## 0.3.3
- Skill mastery: +skill gear can push a skill past 100, and every 5 points over 100 now adds 1% (counted smoothly):
  bows draw faster, crossbows reload faster, every other weapon skill (and unarmed and magic) attacks faster,
  and Blocking adds block power. The Character Sheet shows your mastery.

## 0.3.2
- New command: lla-affix <affix> [tier] spawns an item that has that affix (e.g. lla-affix echo, lla-affix explosive-shot rare).
- Piercing arrows hit each creature only once and keep flying; with Debug logging on, the log lists each creature an arrow pierces.
- Unique light beams on the ground are copper-gold.
- The Unique icon glint is thinner and quicker, every 20 seconds.

## 0.3.1
- Fixed: extra arrows never fired from bows and crossbows (Echo, Explosive Shot, Eaglehorn and Windforce volleys).
- Unique descriptions show their real numbers ("20% chance on hit...").
- Data files in the config folder now update themselves with mod updates when you haven't edited them (older copies are saved as .bak once). Edited files are kept.
- Rare is now a lemon yellow, clearly apart from Unique gold.
- Unique items' icon backgrounds get a gold glint that sweeps across every 12 seconds.

## 0.3.0
- Attack speed: the Attack Speed affix and the "attackSpeed" buff play attack animations faster (hits land sooner). On bows it also speeds up the draw, on crossbows the reload.
- Quick Draw: draw bows and reload crossbows faster.
- New trigger onShoot, and new effects extraProjectiles (fanned volleys, optional pierce), pierce, explosive and meteor.
- Switched on: Echo, Explosive Shot, Quick Draw, Attack Speed, and the Uniques Eaglehorn, Windforce, Skystrike and Bear's Rage.
- Character Sheet shows attack speed, and draw or reload speed with a bow or crossbow.
- powers.yml in your config folder now picks up newly coded powers automatically (your own entries still win; switch one off with an empty list, e.g. `echo: []`).
- Console commands now use hyphens and any capitals: lla-drop, lla-tier, lla-unique, lla-kill100. Unique and biome names ignore capitals, spaces and punctuation (lla-unique jarl-slayer, lla-drop deep-north).

## 0.2.9
- The glint across Unique gold now sweeps once every 6 seconds.

## 0.2.8
- Fixed: the Unique gold shine wasn't showing. The title now gilds as a whole, and the "Unique" label is found by a marker color instead of a link tag.

## 0.2.7
- Unique gold is much bolder: near-white to dark bronze, a gentle ripple of light that's always moving, and a brighter, more frequent glint.
- Fixed the "LiberationSans SDF Font Asset was not found" warning from the "Scroll for more" note.

## 0.2.6
- Unique items: the name and the "Unique" label shine in a gold gradient, with a foil-style glint sweeping across every few seconds.
- Scrolling tooltips: the bottom cut-off is now measured from the tooltip box's real bottom edge, so text fades out well above the "Scroll for more" note.
- "Scroll for more" sits on a soft black glow, so it stays readable over anything behind it.

## 0.2.5
- Scrolling tooltips: the text now fades out above a clear band at the bottom, so it never runs into the frame or the "Scroll for more" note.
- Fixed: scrolling tooltips squeezed the text into a narrow column.
- The light shaft's flicker is much subtler.

## 0.2.4
- Long tooltips scroll smoothly instead of turning pages, with the text fading out at an edge where there's more to read. Mouse wheel or right stick.
- The light shaft is now a pillar of holy fire: a soft flame (wide and faint at the base, bright higher up, tapering to a point) with a whiter core, flickering gently. Uniques keep their sparkles.

## 0.2.3
- Tooltip fonts: thinner outline and no letter thickening, so headings stay Bold (not Black) and the italic keeps its shape.
- Uniques on the ground: a wider halo, gold sparkles drifting up the beam, and a slowly pulsing glow. New sound: uniqueitemsound.ogg.
- Crits: 5% base chance, cap raised to 55%. Crits no longer add extra stagger.
- New Character Sheet page at the top of the Compendium: crit, armor, resistances, immunities, health/stamina/eitr, carry weight, Unique Find, movement and stamina costs, Unique powers, and every bonus from gear.

## 0.2.2
- Thin black outline on the Designed tooltip fonts, like Valheim's own font.

## 0.2.1
- Fixed: missing spaces in italic text (Unique descriptions, flavor text), caused by the italic font's kerning data.
- Fixed: crit numbers showed raw color codes. They're now gold, larger and end in "!".

## 0.2.0 (step 2 begins)
- Powers system: affixes and Uniques are defined in powers.yml as trigger + conditions + effects, and reload live.
- Critical strikes: chance and damage from gear (caps: 50% chance, 2.5x damage), direct hits only, not on sneak attacks. Your crits show in gold.
- 17 more affixes switched on: Lifesteal, Heal on Kill, Slow, Paralyze, Thorns, Momentum, Vampiric Parry, Executioner, Stagger Damage, Backstab, Opportunist, Perforation, Pain Tolerance, Turtle Shell, Warmth, plus Crit Chance and Crit Damage.
- 18 Uniques can now drop: Allfather's Hood, Surtling Mantle, Ymir's Hide, Troll's Strength, Prismatic Defender, Steadfast Cuirass, Wanderer's Leggings, Berserker, Impenetrable, Undying, Jarl-Slayer, Bloodaxe, Logbreaker, First Cut, Reaper's Arc, Fenrir's Hunger, Huntsman's Snare, Iron Moor.
- New command: lla_unique <UniqueId>.

## 0.1.16
- Controller: right stick down or right turns to the next tooltip page, up or left goes back.

## 0.1.15
- Fixed: flipping to page 2 of a long tooltip jumped back to page 1 straight away.

## 0.1.14
- Fixed: very long tooltips shook up and down. Item tooltips are now capped at 80% of the screen height (configurable); longer ones split into pages, flipped with the mouse wheel or the right stick.

## 0.1.13
- Deep North gear can drop: Nord, Frostfire and Thunderblood weapons, staffs, Voidcaller, armor, capes, shields and trinkets (placed in the Deep North by hand, since they're cast rather than crafted).
- Deep North materials added, so modded items made from them land in the right biome.
- Blob Bomb: Pulp, Ember Charge, Grappling Hook, Snowball, Snow Shovel and Salvaged Lantern never drop.
- Older loot.yml copies now pick up new built-in entries automatically.

## 0.1.12
- Fixed: item tooltip text ran into the frame. Text now has 20px of clear space inside the frame on every side, and the box grows to fit.

## 0.1.11
- New config setting: Tooltip texture strength (default 30%), applied live.
- Custom texture pattern: put tooltip_texture.png in the textures folder to replace just the rock pattern.

## 0.1.10
- Softer rock texture in the tooltip background (30% strength).

## 0.1.9
- Tooltip background is now a close-up jagged rock texture in the tier color: solid at the bottom, fading to black at the top on a slight angle.

## 0.1.8
- Magic item tooltips: a distressed inner glow in the tier color just inside the bronze frame, over a darker, even tier-colored background.

## 0.1.7
- Tooltip text in small caps (except the italic flavor text); numbers 15% smaller.
- More padding inside item tooltips so text clears the frame.
- Custom tooltip art: put tooltip_background.png and/or tooltip_frame.png in the mod's textures folder.

## 0.1.6
- Fixed: the Designed tooltip fonts didn't load (fonts from the same family clashed when registering).
- The log now says how many tooltip fonts loaded.

## 0.1.5
- New tooltip fonts: Spectral SC for names, headings, labels, descriptions and affixes, and Faculty Glyphic for numbers. Keen Combat's skill section is styled to match.
- The Cinzel font is replaced; the "Tooltip font" setting is now Designed, Norse or Vanilla.

## 0.1.4
- Fixed: the tooltip frame and tier wash now wrap the whole tooltip box instead of a tiny spot at the cursor.
- Tighter spacing above the affix section.

## 0.1.3
- Diablo-style item tooltips: worn bronze frame, faint weathered wash in the tier color, and the Cinzel font (bundled, free). Font and frame can be changed in the config.
- Weathered texture on the inventory gradients.
- The hotbar now shows the tier gradient too.

## 0.1.2
- Tankards, the Abyssal Harpoon and the Butcher Knife no longer drop.

## 0.1.1
- Fixed: most creatures (Greydwarfs, Boars, Draugr...) never dropped gear. Their loot drops from the body when it fades, and gear now drops with it.
- DLC items (Cape of Odin) no longer drop.
- Bombs and other consumables can no longer drop or roll affixes.
- Magic items get a tier-colored gradient behind their icon in inventories and chests.
- New sound when a magic item drops: put magicdrop.ogg in the mod's audio folder.

## 0.1.0 (first test build)
- Six loot tiers with random affixes, dropped by creatures using the biome, star and boss rules from the design.
- Personal loot: drops are reserved for their owner for 5 minutes (configurable).
- Bosses drop 2 items per nearby player.
- Crafted gear rolls a tier and affixes; upgrading keeps them.
- 50+ working affixes: weapon damage, armor, resistances, health/stamina/eitr, regen, movement and stamina costs, skills, carry weight, Unique Find.
- Light shafts and tier-colored names; affix lines in tooltips (placed above Keen Combat's skill section).
- Data files (affixes, Uniques, loot tables) synced from the server and reloaded live when edited.
- Testing commands: lla_drop, lla_tier, lla_kill100.
