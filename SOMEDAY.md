# Someday list

Things noticed during testing that we've decided not to fix right now.

- **Lox coat tints the player blue.** The vanilla Lox chestpiece renders the character grey-blue when worn (its
  model logs `Material 'LoxChest' with Shader 'Custom/Creature' doesn't have a texture property '_ChestTex'`).
  This is the base game, not the mod; the coat stays in the loot pool.
- **Italic flavor text falls back to Valheim's font** instead of Spectral SC Italic.
- **Protective Trinket's values** need splitting into two columns in the Sheet.
- **Sheet Unique descriptions** are behind the mod's (the mod's now show real numbers). Update the Sheet before
  regenerating the data files from it, or the old wording comes back.
- **Life Siphon balance.** Works as designed (drain 3 s, hitters heal 5%); may need tuning down later.

# Planned (after the affixes and the rest of the mod)
- Next: new feasts that give Eitr before the Mistlands, for players with mods that add magic early.

- **Quest list reroll.** A way to reroll a vendor's quest offers, like the shop's reroll. Pushed to someday on
  2026-11-20.

# Done (moved from this list)

- **Crafting search** (0.0.50-0.0.52): built as a search box above the crafting list rather than a third tab.
  Controller: the right stick focuses it; Steam's keyboard in Big Picture / SteamOS, the mod's own on-screen
  keyboard elsewhere.
