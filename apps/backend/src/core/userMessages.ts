/**
 * Plain-language text for failures the person using the app can see. The exact reason (code, paths, hashes) always stays in
 * the run log and the manifest; this only decides what the primary message says. A message that is already written for
 * people (the world randomizer preparation, custom enemy pool refusals) is passed through unchanged.
 */

const exact: Record<string, string> = {
  WORKSPACE_UNSAFE: 'The app could not find a safe place to work. Restart the app and try again.',
  GAME_INVALID: 'This folder does not look like a complete Dark Souls Remastered installation. Verify the game files in Steam, or choose the game folder again.',
  PRISTINE_IMPORT_REQUIRED: 'Dark Souls Remastered has not been checked yet. Wait a moment and try again.',
  PRISTINE_SOURCE_UNSAFE: 'Your game files could not be verified. Verify the game files in Steam, then try again.',
  PRISTINE_SOURCE_INVALID: 'Your game files could not be verified. Verify the game files in Steam, then try again.',
  COMBINED_OUTPUT_MISMATCH: 'The randomized files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  COMBINED_OUTPUT_INVALID: 'The randomized files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  FOG_OUTPUT_MISMATCH: 'The randomized world files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  FOG_OUTPUT_INVALID: 'The randomized world files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  FOG_BRIDGE_INPUT_INVALID: 'The world randomizer could not be set up with these settings. Try again, or change the world settings.',
  FOG_STAGING_FAILED: 'The world randomizer could not be set up. Check that the disk has free space and try again.',
  FOG_RETRIES_EXHAUSTED: 'The world randomizer could not find a working layout for this seed and these settings. Try another seed, or switch off some world options.',
  FOG_SEED_UNSOLVABLE: 'The world randomizer could not find a working layout for this seed and these settings. Try another seed, or switch off some world options.',
  FOG_UPSTREAM_INCOMPATIBLE: 'These item and world settings cannot be combined yet. Your game was not changed. Switch off the world randomizer or some extra item categories (NPC gifts, enemy drops, shops) and try again.',
  FOG_ITEM_RULES_UNREADABLE: 'The world randomizer data could not be read. Click Randomize again; if it keeps failing, restart the app.',
  COMBINED_RETRIES_EXHAUSTED: 'No working combination was found for this seed. Try another seed.',
  MANIFEST_WRITE_FAILED: 'Your randomized files could not be saved. Check that the disk has free space and try again.',
};

/** Codes whose message was already written for people (they may still carry a technical `CODE:` prefix to strip). */
const readable = /^(WORLD_DATA_|FOG_PREREQUISITES_MISSING|ENEMY_PLACEMENT_CUSTOM_POOL_INVALID|ENEMY_PLACEMENT_INFEASIBLE)/u;

export const genericFailureMessage = 'Something went wrong while creating your randomized game. Try another seed; the details are under Advanced.';

export const genericWorldFailureMessage = 'The world randomizer stopped with an unexpected problem. Your game was not changed. Try again; if it keeps happening, switch off the world randomizer. The details are under Advanced.';

export function userMessageFor(code: string, technical: string): string {
  if (exact[code]) return exact[code];
  if (readable.test(code)) return technical.replace(/^[A-Z][A-Z0-9_]*:\s*/u, '');
  if (code.startsWith('FOG_')) return genericWorldFailureMessage;
  return genericFailureMessage;
}
